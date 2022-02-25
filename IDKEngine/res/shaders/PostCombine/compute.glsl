#version 460 core

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D Sampler0;
layout(binding = 1) uniform sampler2D Sampler1;
layout(binding = 2) uniform sampler2D Sampler2;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 color = texture(Sampler0, uv).rgb;
    color += texture(Sampler1, uv).rgb;
    color += texture(Sampler2, uv).rgb;

    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}
