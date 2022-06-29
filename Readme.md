# IDKEngine

This is some OpenGL stuff I've been experimenting with lately.
I am using a lot of modern OpenGL features like Direct State Access, Indirect Multi Drawing or Bindless Textures to create a efficient renderer.

Feature list:
 - HDR + Gamma correction
 - ImGui
 - Depth Pre Pass
 - Multithreaded model loader
 - (Copied) Atmospheric Scattering, ported to Compute Shader and precomputed
 - Shadow Samplers for hardware filtering + PCF
 - Screen Space Reflections
 - Screen Space Ambient Occlusion
 - Single pass Vertex Layered Rendering for point shadows
 - GPU accelerated Frustum Culling for shadows and camera
 - Ray marched Volumetric Lighting
 - Variable Rate Shading
 - Temporal Anti Aliasing
 - CoD-Modern-Warfare Bloom
 - Multi Draw Indirect + Bindless Texture system that draws every loaded model in one draw call
 - Path Tracer (WIP<sup>[1](#f1)</sup>)
 
Required OpenGL: 4.6 + `ARB_bindless_texture` and (`NV_gpu_shader5` or `ARB_shader_ballot`)

<font size="1"><b id="f1">1</b>
In `Model.cs` load with `PostProcessSteps.OptimizeGraph | PostProcessSteps.OptimizeMeshes` for PT fps boost. This however will make the rasterizer slightly slower because less meshes can be culled
</font>

# Path Traced Render Samples

![PTTempleDark](Screenshots/PTTemple.png?raw=true)
![PTSponza](Screenshots/PTSponza.png?raw=true)
![PTHorse](Screenshots/PTHorse.png?raw=true)

# Random things

## Variable Rate Shading

### 1.0 Overview

Variable Rate Shading is when you render different regions of the framebuffer at different resolutions. This feature is exposed in OpenGL through `NV_shading_rate_image`. The implementation expects a `R8ui` texture where each pixel covers
a 16x16 tile of the framebuffer. The hardware then fetches this texture looks up the value in a user defined shading rate palette and applies that shading rate to the block of fragments saving us processing power. So all we have to do is generate the shading rate image.

Here is a generated shading image while the camera was moving:

![ShadingRateImage](Screenshots/VRSExample.png?raw=true)

Red stands for 1 invocation per 4x4 pixels which means 4x less fragment shader invocations in those regions.
No color is the default - 1 invocation per pixel.

### 2.0 Shading Rate Image generation

The ultimate goal of the algorithm should be to apply a as low as possible shading rate without the user noticing. I assume the following are cases where we can safely reduce the shading rate:

* High average magnitude of velocity
* Low variance of luminance

This makes sense as you can generally see less detail in fast moving things. And if the luminance across a tile is roughly the same (this is what the variance tells you) there is not much detail to begin with.

In both cases we need the average of some value over all 256 pixels.

Average is defined as:

$$\overline{x} = \sum_{i = 1}^{n} \frac{1}{n} \cdot x_{i}$$

Where $\overline{x}$ is the mean of $x$ and $n$ the number of elements.

For starters you might call `atomicAdd(SharedMem, value * (1.0 / n))` on shared memory however atomics on floats are not a core feature and as far as my testing goes the following approach wasn't any slower:
```glsl
#define TILE_SIZE 16
layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);
shared float Average[TILE_SIZE * TILE_SIZE];
void main()
{
    Average[gl_LocalInvocationIndex] = GetSpeed();
    for (int cutoff = (TILE_SIZE * TILE_SIZE) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            Average[gl_LocalInvocationIndex] += Average[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }
    // average is computed and stored in Average[0]
}
```
The algorithm first loads all the values of the set which we want to compute the average of into shared memory.
Then the first half of the values are summed up with the other half. After that all the summed up values are further split into half and again added with the rest. At some point the final sum will have been collapsed into the first element.

That's it for the averaging part.

Calculating the variance requires some more work.

(Normalized) Variance is defined as:

$$V(x) = \sum_{i = 1}^{n}(x_{i} - \overline{x})^{2} \times \frac{1}{n - 1}$$
<!-- fix error of second formula not rendering properly -->
&nbsp;
$$VN = \frac{\sqrt{V(x)}}{\overline{x}}$$

Like before $\overline{x}$ is the mean of set $x$ and $n$ the number of elements. $V(x)$ tells us the variance of that set.
However $V(x)$ is dependent on scale which is not what we want. {5, 10} should result in the same variance as {10, 20}.
The second part solves this by [normalizing the variance](https://www.vosesoftware.com/riskwiki/Normalizedmeasuresofspread-theCofV.php).

Here's a implementation. I put the parallel adding stuff from above in the function `ParallelSum`.
```glsl
#define TILE_SIZE 16
layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);
const float VARIANCE_AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE - 1);

void main()
{
    float lumAvg = ParallelSum(GetLuminance() * AVG_MULTIPLIER);
    
    float deltaLumAvg = pow(luminance - lumAvg, 2.0);
    float varianceLum = ParallelSum(deltaLumAvg * VARIANCE_AVG_MULTIPLIER);

    if (gl_LocalInvocationIndex == 0)
    {
        // use this as a measure of "how different is luminance in this tile"
        float normalizedVariance = sqrt(varianceLum) / deltaLumAvg;

        // compute and output final shading rate
    }
}
```

At this point using both average speed and variance of luminance you can obtain an appropriate shading rate. This is not the most interesting part.
I decided to scale both of these factors add them together and then use that to mix between different rates. You can find the code [here](https://github.com/BoyBaykiller/IDKEngine/blob/master/IDKEngine/res/shaders/ShadingRateClassification/compute.glsl).

### 3.0 Subgroup optimizations

While operating on shared memory is fast Subgroup Intrinsics are faster.
They are a relatively new topic on its own and you almost never see them mentioned in the context of OpenGL. The subgroup is an implementation dependent set of invocations in which data can be shared efficiently. There are a lot of subgroup operations. The full thing is document [here](https://github.com/KhronosGroup/GLSL/blob/master/extensions/khr/GL_KHR_shader_subgroup.txt) but vendor specific/arb extensions with `ARB_shader_group_vote` actually being part of core also exist.
Anyway the one which is particular interesting for our case of computing a sum is `KHR_shader_subgroup_arithmetic` or more specifically the function `subgroupInclusiveAdd`.

On my GPU a subgroup is 32 invocations big.
If I call `subgroupInclusiveAdd(2)` the function will return 64.
So it basically does a sum over values passed into the function in the scope of a subgroup.

Using this knowledge my optimized version of a workgroup wide sum looks like this:
```glsl
#define SUBGROUP_SIZE __subroupSize__
#extension GL_KHR_shader_subgroup_arithmetic : require

layout(local_size_x = 256, local_size_y = 1, local_size_z = 1) in;

shared float SharedSums[gl_WorkGroupSize.x / SUBGROUP_SIZE];
void main()
{
    float subgroupSum = subgroupInclusiveAdd(GetValueToAdd());

    // single invocation of the subgroup should write result
    // into shared mem for further workgroup wide processing
    if (subgroupElect())
    {
        SharedSums[gl_SubgroupID] = subgroupSum;
    }
    barrier();

    // finally add up all sums previously computed by the subgroups in this workgroup
    for (int cutoff = gl_NumSubgroups / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedSums[gl_LocalInvocationIndex] += SharedSums[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }

    // average is computed and stored in SharedSums[0]
}
```
Note how the workgroup expands the size of a subgroup so we still have to use shared memory to obtain a workgroup wide result.
However in the for loop it now only has to iterate through `log2(gl_NumSubgroups / 2)` values instead of `log2(gl_WorkGroupSize.x / 2)`.

## Point Shadows

### 1.0 Rendering

In core OpenGL geometry shaders are the only stage where you can write to `gl_Layer`. This variable specifies which layer of the framebuffer attachment to render to.
There is a common approach to point shadow rendering where
instead of: 
```cs
for (int face = 0; face < 6; face++)
{
    NamedFramebufferTextureLayer(fbo, attachment, cubemap, level, face);
    // Render Scene into <face>th face of cubemap
    RenderScene();
}
```
you do:

```cs
RenderScene();
```
```glsl
void main()
{
    for (int face = 0; face < 6; face++)
    {
        gl_Layer = face;
        OutputTriangle();
    }
}
```
Notice how instead of calling `NamedFramebufferTextureLayer` from the CPU we set `gl_Layer` inside a geometry shader saving us 5 draw calls and driver overhead.

So this should be faster right?

No. At least not on my RX 580 and RX 5700 XT. And I assume its not much different on NVIDIA since geometry shaders are slow except on Intel.

There are multiple extensions which allow you to set `gl_Layer` from a vertex shader.
`ARB_shader_viewport_layer_array` for example reads:
> In order to use any viewport or attachment layer other than zero, a
> geometry shader must be present. Geometry shaders introduce processing
> overhead and potential performance issues. The `AMD_vertex_shader_layer`
> and `AMD_vertex_shader_viewport_index` extensions allowed the `gl_Layer`
> and `gl_ViewportIndex` outputs to be written directly from the vertex shader
> with no geometry shader present.
> This extension effectively merges ...

So what I ended up doing is either use vertex layered rendering if any of the required extensions is available and else just do 6 draw calls without invoking any geometry shader since that is slower.

The way you might do vertex layered rendering looks like this:
```cs
RenderSceneInstanced(count: 6);
```

```glsl
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_AMD_vertex_shader_layer : enable
#extension GL_NV_viewport_array : enable
#extension GL_NV_viewport_array2 : enable
#define IS_VERTEX_LAYERED_RENDERING (GL_ARB_shader_viewport_layer_array || GL_AMD_vertex_shader_layer || GL_NV_viewport_array || GL_NV_viewport_array2)


void main()
{
#if IS_VERTEX_LAYERED_RENDERING
    gl_Layer = gl_InstanceID;
    gl_Position = PointShadow.FaceMatrices[gl_Layer] * Positon;
#else
    // fallback
#endif
}
```
Since vertex shaders can't generate vertices like geometry shader we'll use a instanced draw command to tell OpenGL to render every vertex 6 times. Inside the shader we can then use the current instance (`gl_InstanceID`) as the face to render to.

### 2.0 Sampling

Just want to mention that OpenGL provides useful shadow sampler types like `samplerCubeShadow` or `sampler2DShadow`.
When using shadow samplers texture lookup functions accept an additional parameter with which the depth value in the texture
is compaired. Also the returned value is no longer the depth value but instead a visibility ratio in the range of [0, 1].
When using linear filtering the compairson is evaluted and averaged for a 2x2 block of pixels.
And all that in one function!

To configure a texture such that it can be sampled by a shadow sampler you can do this:
```cs
GL.TextureParameter(texture, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
GL.TextureParameter(texture, TextureParameterName.TextureCompareFunc, (int)All.Less);
```
And sample it in glsl like that:
```glsl
layout(binding = 0) uniform samplerCubeShadow SamplerShadow;

void main()
{
    float fragDepth = GetDepthRange0_1(); 
    float visibility = texture(SamplerShadow, vec3(coords, fragDepth));
}
```

Here is a comparison of using shadow samplers (right) vs not using them.
![SamplingComparison](Screenshots/SamplingComparison.png?raw=true)

Obviously you can combine this with software filtering like PCF to get even better results.

## GPU Driven Rendering

### 1.0 Multi Draw Indirect

This "Engine" does a modern low overhead MDI approach to rendering.
MDI stands for multi draw indirect. The most commonly used MDI function is probably `MultiDrawElementsIndirect`.
As the name suggests it does multiple draws under the hood. In fact `MultiDrawElementsIndirect` is equivalent
to calling `DrawElementsInstancedBaseVertexBaseInstance` in a for loop with `drawcount` iterations.
Arguments for these underlying draw calls are provided by us and expected to have the following format:
```cs
struct DrawCommand
{
    int Count; // indices count
    int InstanceCount; // number of instances
    int FirstIndex; // offset in indices array
    int BaseVertex; // offset in vertex array
    int BaseInstance; // unimportant unless you're doing old school instancing - can be useful for own purposes
}
```
The way you supply the draw commands to the API is with a buffer - that's what the "indirect" suffix says.
So to render 5 meshes you'd need to put all of their vertices and indices into two single big arrays and
configure 5 `DrawCommand` structs and upload them to a buffer.

The final draw could then be as simple as:
```cs
public void Draw()
{
    vao.Bind(); // contains big vertex and indices array + vertex format
    drawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer); // contains DrawCommand[Meshes.Length]

    GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, (IntPtr)0, Meshes.Length, 0);
}
```
While this renders all geometry just fine you might be wondering how to access the entirety of materials to compute proper shading. After all scenes like Sponza come with a lot of textures and the usual method of manually declaring `sampler2D` in glsl quickly becomes insufficient as we can't do state changes between draw calls anymore (which is good).

### 2.0 Bindless Textures

This is where Bindless Textures comes in...
