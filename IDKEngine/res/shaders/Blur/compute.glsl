#version 460 core

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0) uniform sampler2D SamplerSrc;
layout(binding = 0, rgba16f) restrict writeonly uniform image2D ImgResult;

// Optimized to abuse the bilinear filtering hardware
// Source: https://www.rastergrid.com/blog/2010/09/efficient-gaussian-blur-with-linear-sampling/
const float offsets[3] = { 0.0, 1.3846153846, 3.2307692308 };
const float weights[3] = { 0.2270270270, 0.3162162162, 0.0702702703 };

layout(location = 0) uniform bool IsHorizontalPass;

void main()
{
    vec2 imgCoord = vec2(gl_GlobalInvocationID.xy) + 0.5;
    vec2 size = imageSize(ImgResult);

    vec3 color = texture(SamplerSrc, imgCoord / size).rgb * weights[0];
    
    if (IsHorizontalPass)
    {
        for (int i = 1; i < 3; i++)
        {
            color += texture(SamplerSrc, (imgCoord + vec2(offsets[i], 0.0)) / size).rgb * weights[i];
            color += texture(SamplerSrc, (imgCoord - vec2(offsets[i], 0.0)) / size).rgb * weights[i];
        }
    }
    else    
    {
        for (int i = 1; i < 3; i++)
        {
            color += texture(SamplerSrc, (imgCoord + vec2(0.0, offsets[i])) / size).rgb * weights[i];
            color += texture(SamplerSrc, (imgCoord - vec2(0.0, offsets[i])) / size).rgb * weights[i];
        }
    }

    imageStore(ImgResult, ivec2(imgCoord), vec4(color, 1.0));
}
