// Source: http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare
// Slides: 145-162

#version 460 core
#define EPSILON 0.0001
#define DOWNSAMPLE_STAGE 0
#define UPSAMPLE_STAGE 1
#extension GL_AMD_shader_trinary_minmax : enable

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerDownsample;
layout(binding = 1) uniform sampler2D SamplerUpsample;

vec3 Downsample(vec2 uv);
vec3 Upsample(vec2 uv);
vec3 Prefilter(vec3 color);

uniform float Threshold;
uniform float Clamp;
uniform float Radius;

layout(location = 3) uniform int Lod;
layout(location = 4) uniform int Stage;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 imgSize = imageSize(ImgResult);
    if (any(greaterThanEqual(imgCoord, imgSize)))
        return;

    vec3 result = vec3(0.0);
    vec2 uv = (imgCoord + 0.5) / imgSize;
    
    if (Stage == DOWNSAMPLE_STAGE)
    {
        result = Downsample(uv);
        if (Lod == 0)
            result = Prefilter(result);
    }
    else if (Stage == UPSAMPLE_STAGE)
    {
        result = Upsample(uv) + textureLod(SamplerDownsample, uv, Lod - 1).rgb;
    }
    imageStore(ImgResult, imgCoord, vec4(result, 1.0));
}

vec3 Downsample(vec2 uv)
{
    vec3 center = textureLod(SamplerDownsample, uv, Lod).rgb;
    vec3 yellowUpRight  = textureLodOffset(SamplerDownsample, uv, Lod, ivec2( 0,  2)).rgb;
    vec3 yellowDownLeft = textureLodOffset(SamplerDownsample, uv, Lod, ivec2(-2,  0)).rgb;
    vec3 greenDownRight = textureLodOffset(SamplerDownsample, uv, Lod, ivec2( 2,  0)).rgb;
    vec3 blueDownLeft   = textureLodOffset(SamplerDownsample, uv, Lod, ivec2( 0, -2)).rgb;

    vec3 yellow  = textureLodOffset(SamplerDownsample, uv, Lod, ivec2(-2,  2)).rgb;
    yellow      += yellowUpRight;
    yellow      += center;
    yellow      += yellowDownLeft;

    vec3 green = yellowUpRight;
    green     += textureLodOffset(SamplerDownsample, uv, Lod, ivec2( 2,  2)).rgb;
    green     += greenDownRight;
    green     += center;

    vec3 blue = center;
    blue     += greenDownRight;
    blue     += textureLodOffset(SamplerDownsample, uv, Lod, ivec2( 2, -2)).rgb;
    blue     += blueDownLeft;

    vec3 lila  = yellowDownLeft;
    lila      += center;
    lila      += blueDownLeft;
    lila      += textureLodOffset(SamplerDownsample, uv, Lod, ivec2(-2, -2)).rgb;

    vec3 red = textureLodOffset(SamplerDownsample, uv, Lod, ivec2(-1,  1)).rgb;
    red     += textureLodOffset(SamplerDownsample, uv, Lod, ivec2( 1,  1)).rgb;
    red     += textureLodOffset(SamplerDownsample, uv, Lod, ivec2( 1, -1)).rgb;
    red     += textureLodOffset(SamplerDownsample, uv, Lod, ivec2(-1, -1)).rgb;

    return (red * 0.5 + (yellow + green + blue + lila) * 0.125) * 0.25;
}

vec3 Upsample(vec2 uv)
{
    vec3 result = textureLodOffset(SamplerUpsample, uv, Lod, ivec2(-1.0, 1.0)).rgb * 1.0;
    result     += textureLodOffset(SamplerUpsample, uv, Lod, ivec2( 0.0, 1.0)).rgb * 2.0;
    result     += textureLodOffset(SamplerUpsample, uv, Lod, ivec2( 1.0, 1.0)).rgb * 1.0;

    result     += textureLodOffset(SamplerUpsample, uv, Lod, ivec2(-1.0, 0.0)).rgb * 2.0;
    result     += textureLod(SamplerUpsample, uv, Lod).rgb * 4.0;
    result     += textureLodOffset(SamplerUpsample, uv, Lod, ivec2( 1.0, 0.0)).rgb * 2.0;
    
    result     += textureLodOffset(SamplerUpsample, uv, Lod, ivec2(-1.0, -1.0)).rgb * 1.0;
    result     += textureLodOffset(SamplerUpsample, uv, Lod, ivec2( 0.0, -1.0)).rgb * 2.0;
    result     += textureLodOffset(SamplerUpsample, uv, Lod, ivec2( 1.0, -1.0)).rgb * 1.0;

    return result * (1.0 / 16.0);
}

vec3 Prefilter(vec3 color)
{
    const float Knee = 0.2;
    color = min(vec3(Clamp), color);

#ifdef GL_AMD_shader_trinary_minmax
    float brightness = max3(color.r, color.g, color.b);
#else
    float brightness = max(max(color.r, color.g), color.b);
#endif

    vec3 curve = vec3(Threshold - Knee, Knee * 2.0, 0.25 / Knee);
    float rq = clamp(brightness - curve.x, 0.0, curve.y);
    rq = (rq * rq) * curve.z;
    color *= max(rq, brightness - Threshold) / max(brightness, EPSILON);
    
    return color;
}
