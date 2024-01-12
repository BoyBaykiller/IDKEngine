#version 460 core

layout(binding = 0) uniform sampler2D SamplerDepth;

layout(location = 0) uniform int Lod;

in InOutVars
{
    vec2 TexCoord;
} inData;

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);

    ivec2 vReadCoord = imgCoord * 2;

    vec4 samples;
    samples.x = texelFetch(SamplerDepth, vReadCoord + ivec2(0, 0), Lod).r;
    samples.y = texelFetch(SamplerDepth, vReadCoord + ivec2(1, 0), Lod).r;
    samples.z = texelFetch(SamplerDepth, vReadCoord + ivec2(0, 1), Lod).r;
    samples.w = texelFetch(SamplerDepth, vReadCoord + ivec2(1, 1), Lod).r;

    float furthestDepth = max(samples.x, max(samples.y, max(samples.z, samples.w)));

    vec2 srcSize = textureSize(SamplerDepth, Lod);
    vec2 resultSize = textureSize(SamplerDepth, Lod + 1);
    vec2 sizeRatio = srcSize / resultSize;

    bool needExtraSampleX = sizeRatio.x > 2;
    bool needExtraSampleY = sizeRatio.y > 2;

    if (needExtraSampleX)
    {
        vec2 additionalSamples;
        additionalSamples.x = texelFetch(SamplerDepth, vReadCoord + ivec2(2, 0), Lod).r;
        additionalSamples.y = texelFetch(SamplerDepth, vReadCoord + ivec2(2, 1), Lod).r;

        furthestDepth = max(furthestDepth, max(additionalSamples.x, additionalSamples.y));
    }
    if (needExtraSampleY)
    {
        vec2 additionalSamples;
        additionalSamples.x = texelFetch(SamplerDepth, vReadCoord + ivec2(0, 2), Lod).r;
        additionalSamples.y = texelFetch(SamplerDepth, vReadCoord + ivec2(1, 2), Lod).r;

        furthestDepth = max(furthestDepth, max(additionalSamples.x, additionalSamples.y));
    }
    if (needExtraSampleX && needExtraSampleY)
    {
        float additionalSample = texelFetch(SamplerDepth, vReadCoord + ivec2(2, 2), Lod).r;
        furthestDepth = max(furthestDepth, additionalSample);
    }

    gl_FragDepth = furthestDepth;
}