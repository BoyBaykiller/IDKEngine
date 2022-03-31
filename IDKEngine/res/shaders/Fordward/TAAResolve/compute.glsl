#version 460 core

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerLastForwardPass;
layout(binding = 1) uniform sampler2D SamplerVelocity;

layout(std140, binding = 5) uniform TaaDataUBO
{
    vec4 Jitters[32 / 2];
    int Samples;
    int Enabled;
    int Frame;
} taaDataUBO;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 color = imageLoad(ImgResult, imgCoord).rgb;

    float blend = 1.0 / (taaDataUBO.Enabled == 0 ? 1.0 : taaDataUBO.Samples);
    
    vec2 oldUV = uv - texture(SamplerVelocity, uv).rg;
    vec3 history = texture(SamplerLastForwardPass, oldUV).rgb;
    
    if (any(greaterThan(oldUV, vec2(1.0))) || any(lessThan(oldUV, vec2(0.0))))
    {
        blend = 1.0;
    }

    color = mix(history, color, blend);

    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}