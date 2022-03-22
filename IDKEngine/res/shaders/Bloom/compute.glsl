#version 460 core

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerSrc;

vec3 DownSample(vec2 upperRight);
vec3 Upsample(vec2 upperRight);

layout(location = 0) uniform int Lod;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    vec2 upperRight = (imgCoord + 1.0) / imageSize(ImgResult);

    vec3 result = DownSample(upperRight);
    imageStore(ImgResult, imgCoord, vec4(result, 1.0));
}

// Source: http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare
// Slide 153
vec3 DownSample(vec2 upperRight)
{
    vec3 center = textureLod(SamplerSrc, upperRight, Lod).rgb;
    vec3 yellowUpRight  = textureLodOffset(SamplerSrc, upperRight, Lod, ivec2( 0,  2)).rgb;
    vec3 yellowDownLeft = textureLodOffset(SamplerSrc, upperRight, Lod, ivec2(-2,  0)).rgb;
    vec3 greenDownRight = textureLodOffset(SamplerSrc, upperRight, Lod, ivec2( 2,  0)).rgb;
    vec3 blueDownLeft   = textureLodOffset(SamplerSrc, upperRight, Lod, ivec2( 0, -2)).rgb;

    vec3 yellow  = textureLodOffset(SamplerSrc, upperRight, Lod, ivec2(-2,  2)).rgb;
    yellow      += yellowUpRight;
    yellow      += center;
    yellow      += yellowDownLeft;

    vec3 green = yellowUpRight;
    green     += textureLodOffset(SamplerSrc, upperRight, Lod, ivec2( 2,  2)).rgb;
    green     += greenDownRight;
    green     += center;

    vec3 blue = center;
    blue     += greenDownRight;
    blue     += textureLodOffset(SamplerSrc, upperRight, Lod, ivec2( 2, -2)).rgb;
    blue     += blueDownLeft;

    vec3 lila  = yellowDownLeft;
    lila      += center;
    lila      += blueDownLeft;
    lila      += textureLodOffset(SamplerSrc, upperRight, Lod, ivec2(-2, -2)).rgb;

    vec3 red = textureLodOffset(SamplerSrc, upperRight, Lod, ivec2(-1,  1)).rgb;
    red     += textureLodOffset(SamplerSrc, upperRight, Lod, ivec2( 1,  1)).rgb;
    red     += textureLodOffset(SamplerSrc, upperRight, Lod, ivec2( 1, -1)).rgb;
    red     += textureLodOffset(SamplerSrc, upperRight, Lod, ivec2(-1, -1)).rgb;

    return (red * 0.5 + (yellow + green + blue + lila) * 0.125) * 0.25;
}

vec3 Upsample(vec2 upperRight)
{
    vec2 texelSize = 1.0 / textureSize(SamplerSrc, Lod);

    // Up
    vec3 result = textureLod(SamplerSrc, upperRight - texelSize * vec2(-1.0, 1.0), Lod).rgb * 1.0;
    result     += textureLod(SamplerSrc, upperRight - texelSize * vec2( 0.0, 1.0), Lod).rgb * 2.0;
    result     += textureLod(SamplerSrc, upperRight - texelSize * vec2( 1.0, 1.0), Lod).rgb * 1.0;

    // Middle
    result     += textureLod(SamplerSrc, upperRight - texelSize * vec2(-1.0, 0.0), Lod).rgb * 2.0;
    result     += textureLod(SamplerSrc, upperRight, Lod).rgb * 4.0;
    result     += textureLod(SamplerSrc, upperRight - texelSize * vec2( 1.0, 0.0), Lod).rgb * 2.0;
    
    // Down
    result     += textureLod(SamplerSrc, upperRight - texelSize * vec2(-1.0, -1.0), Lod).rgb * 1.0;
    result     += textureLod(SamplerSrc, upperRight - texelSize * vec2( 0.0, -1.0), Lod).rgb * 2.0;
    result     += textureLod(SamplerSrc, upperRight - texelSize * vec2( 1.0, -1.0), Lod).rgb * 1.0;

    return result * (1.0 / 16.0);
}