#version 460 core
#extension GL_ARB_bindless_texture : require

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D First;
layout(binding = 1) uniform sampler2D Second;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    vec3 first = texelFetch(First, imgCoord, 0).rgb;
    vec3 second = texelFetch(Second, imgCoord, 0).rgb;

    vec3 color = first + second;
    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}
