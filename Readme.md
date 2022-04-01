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
 - Temporal Anti Aliasing
 - Fast CoD-Modern-Warfare Bloom implementation
 - Multi Draw Indirect + Bindless Texture system that draws every loaded model in one draw call
 - Path Tracer (early progress and no bvh yet so really really slow)
 
Required OpenGL: 4.6

Required extensions: `ARB_bindless_texture` and (`NV_gpu_shader5` or `ARB_shader_ballot`)

Optional extensions: (`ARB_shader_viewport_layer_array` or `AMD_vertex_shader_layer` or `NV_viewport_array` or `NV_viewport_array2`), (`ARB_seamless_cubemap_per_texture` or `AMD_seamless_cubemap_per_texture`), `NV_compute_shader_derivatives`, `AMD_shader_trinary_minmax`

# Render Samples

![PathTracedDiffuse](Screenshots/PathTracedDiffuse.png?raw=true)

![PathTracedShiny](Screenshots/PathTracedShiny.png?raw=true)

![Rasterized](Screenshots/Rasterized.PNG?raw=true)