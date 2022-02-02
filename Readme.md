# IDKEngine

This is some OpenGL stuff I've been experimenting with lately.
I am using a lot of modern OpenGL features like Direct State Access or the whole "Approaching Zero Driver Overhead" thing to create a efficient renderer.

Feature list:
 - ImGui
 - Depth Pre Pass
 - Multithreaded model loader
 - Physically Based Renderer
 - (Copied) Atmospheric Scattering, ported to Compute Shader and precomputed
 - GLSL Shadow Samplers for hardware filtering + PCF
 - Naive Screen Space Reflections (needs more tweaking, disabled on default)
 - Single pass Vertex layered rendering for point shadows (updated every frame for all geometry kinda bad)
 - GPU accelerated Frustum Culling (implement for shadows as well?)
 - Ray marched Volumetric Lighting
 - Multi draw indirect + bindless texture system that draws every loaded model in one draw call
 - Path Tracer (early progress and no bvh yet so really really slow)
 
Required OpenGL: 4.6

Required extensions: `ARB_bindless_texture` and (`EXT_nonuniform_qualifier` or `NV_gpu_shader5`)

Optional extensions: (`ARB_shader_viewport_layer_array` or `AMD_vertex_shader_layer` or `NV_viewport_array` or `NV_viewport_array2`) and (`ARB_seamless_cubemap_per_texture` or `AMD_seamless_cubemap_per_texture`)


![PathTracedDiffuse](Screenshots/PathTracedDiffuse.png?raw=true)

![PathTracedShiny](Screenshots/PathTracedShiny.png?raw=true)

![Rasterized](Screenshots/Rasterized.png?raw=true)