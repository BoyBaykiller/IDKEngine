#version 460 core
#define EPSILON 0.0001
#define FILTER_DOWNSAMPLE_STAGE 0
#define DOWNSAMPLE_STAGE 1
#define UPSAMPLE_STAGE 2

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerDownsample;
layout(binding = 1) uniform sampler2D SamplerUpsample;

vec3 Downsample(vec2 upperRight);
vec3 Upsample(vec2 upperRight);
vec3 QuadraticThreshold(vec3 color, float threshold, float knee);
vec3 Prefilter(vec3 color);

layout(location = 0) uniform int Lod;
layout(location = 1) uniform int Stage;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 upperRight = (imgCoord + 1.0) / imageSize(ImgResult);

    vec3 result = vec3(0.0);
    if (Stage == FILTER_DOWNSAMPLE_STAGE)
    {
        result = Downsample(upperRight);
        result = Prefilter(result);
    }
    else if (Stage == DOWNSAMPLE_STAGE)
    {
        result = Downsample(upperRight);
    }
    else if (Stage == UPSAMPLE_STAGE)
    {
        result = Upsample(upperRight) + textureLod(SamplerDownsample, upperRight, Lod - 1).rgb;
    }
    imageStore(ImgResult, imgCoord, vec4(result, 1.0));
}

// Source: http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare
// Slide 153
vec3 Downsample(vec2 upperRight)
{
    vec3 center = textureLod(SamplerDownsample, upperRight, Lod).rgb;
    vec3 yellowUpRight  = textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2( 0,  2)).rgb;
    vec3 yellowDownLeft = textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2(-2,  0)).rgb;
    vec3 greenDownRight = textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2( 2,  0)).rgb;
    vec3 blueDownLeft   = textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2( 0, -2)).rgb;

    vec3 yellow  = textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2(-2,  2)).rgb;
    yellow      += yellowUpRight;
    yellow      += center;
    yellow      += yellowDownLeft;

    vec3 green = yellowUpRight;
    green     += textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2( 2,  2)).rgb;
    green     += greenDownRight;
    green     += center;

    vec3 blue = center;
    blue     += greenDownRight;
    blue     += textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2( 2, -2)).rgb;
    blue     += blueDownLeft;

    vec3 lila  = yellowDownLeft;
    lila      += center;
    lila      += blueDownLeft;
    lila      += textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2(-2, -2)).rgb;

    vec3 red = textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2(-1,  1)).rgb;
    red     += textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2( 1,  1)).rgb;
    red     += textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2( 1, -1)).rgb;
    red     += textureLodOffset(SamplerDownsample, upperRight, Lod, ivec2(-1, -1)).rgb;

    return (red * 0.5 + (yellow + green + blue + lila) * 0.125) * 0.25;
}

vec3 Upsample(vec2 upperRight)
{
    vec2 texelSize = 1.0 / textureSize(SamplerUpsample, Lod);

    // Up
    vec3 result = textureLod(SamplerUpsample, upperRight - texelSize * vec2(-1.0, 1.0), Lod).rgb * 1.0;
    result     += textureLod(SamplerUpsample, upperRight - texelSize * vec2( 0.0, 1.0), Lod).rgb * 2.0;
    result     += textureLod(SamplerUpsample, upperRight - texelSize * vec2( 1.0, 1.0), Lod).rgb * 1.0;

    // Middle
    result     += textureLod(SamplerUpsample, upperRight - texelSize * vec2(-1.0, 0.0), Lod).rgb * 2.0;
    result     += textureLod(SamplerUpsample, upperRight, Lod).rgb * 4.0;
    result     += textureLod(SamplerUpsample, upperRight - texelSize * vec2( 1.0, 0.0), Lod).rgb * 2.0;
    
    // Down
    result     += textureLod(SamplerUpsample, upperRight - texelSize * vec2(-1.0, -1.0), Lod).rgb * 1.0;
    result     += textureLod(SamplerUpsample, upperRight - texelSize * vec2( 0.0, -1.0), Lod).rgb * 2.0;
    result     += textureLod(SamplerUpsample, upperRight - texelSize * vec2( 1.0, -1.0), Lod).rgb * 1.0;

    return result * (1.0 / 16.0);
}

vec3 QuadraticThreshold(vec3 color, float threshold, float knee)
{
    float brightness = max(max(color.r, color.g), color.b);

    vec3 curve = vec3(threshold - knee, knee * 2.0, 0.25 / knee);
    float rq = clamp(brightness - curve.x, 0.0, curve.y);
    rq = (rq * rq) * curve.z;
    color *= max(rq, brightness - threshold) / max(brightness, EPSILON);
    return color;
}

vec3 Prefilter(vec3 color)
{
    const float threshold = 1.0;
    const float knee = 0.20;
    const float clampValue = 20.0;
    
    color = min(vec3(clampValue), color);
    color = QuadraticThreshold(color, threshold, knee);
    
    return color;
}
