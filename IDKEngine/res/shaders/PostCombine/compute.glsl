#version 460 core

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerForward;
layout(binding = 1) uniform sampler2D SamplerVolumetricLighting;
layout(binding = 2) uniform sampler2D SamplerSSR;

void main()
{
    vec2 uv = (gl_GlobalInvocationID.xy + 0.5) / vec2(imageSize(ImgResult));

    vec3 color = texture(SamplerForward, uv).rgb;
    color += texture(SamplerVolumetricLighting, uv).rgb;
    color += texture(SamplerSSR, uv).rgb;

    imageStore(ImgResult, ivec2(gl_GlobalInvocationID.xy), vec4(color, 1.0));
}
