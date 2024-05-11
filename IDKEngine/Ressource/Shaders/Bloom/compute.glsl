// Implementation of: http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare, Slides: 145-162

#version 460 core
#define DOWNSAMPLE_STAGE 0
#define UPSAMPLE_STAGE 1

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerDownsample;
layout(binding = 1) uniform sampler2D SamplerUpsample;

layout(std140, binding = 7) uniform SettingsUBO
{
    float Threshold;
    float MaxColor;
} settingsUBO;

vec3 Downsample(sampler2D srcTexture, vec2 uv, float lod);
vec3 Upsample(sampler2D srcTexture, vec2 uv, float lod);
vec3 Prefilter(vec3 color, float maxColor, float threshold);

layout(location = 0) uniform int Lod;
layout(location = 1) uniform int Stage;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 imgSize = imageSize(ImgResult);
    if (any(greaterThanEqual(imgCoord, imgSize)))
    {
        return;
    }

    vec3 result = vec3(0.0);
    vec2 uv = (imgCoord + 0.5) / imgSize;
    
    if (Stage == DOWNSAMPLE_STAGE)
    {
        result = Downsample(SamplerDownsample, uv, Lod);
        if (Lod == 0)
        {
            result = Prefilter(result, settingsUBO.MaxColor, settingsUBO.Threshold);
        }
    }
    else if (Stage == UPSAMPLE_STAGE)
    {
        result = Upsample(SamplerUpsample, uv, Lod) + textureLod(SamplerDownsample, uv, Lod).rgb;
    }
    
    // writes to Lod + 1 mip level when downsampling (except when Lod == 0)
    // writes to Lod - 1 mip level when upsampling
    imageStore(ImgResult, imgCoord, vec4(result, 1.0));
}

vec3 Downsample(sampler2D src, vec2 uv, float lod)
{
    vec3 center = textureLod(src, uv, lod).rgb;
    vec3 yellowUpRight  = textureLodOffset(src, uv, lod, ivec2( 0,  2)).rgb;
    vec3 yellowDownLeft = textureLodOffset(src, uv, lod, ivec2(-2,  0)).rgb;
    vec3 greenDownRight = textureLodOffset(src, uv, lod, ivec2( 2,  0)).rgb;
    vec3 blueDownLeft   = textureLodOffset(src, uv, lod, ivec2( 0, -2)).rgb;

    vec3 yellow  = textureLodOffset(src, uv, lod, ivec2(-2,  2)).rgb;
    yellow      += yellowUpRight;
    yellow      += center;
    yellow      += yellowDownLeft;

    vec3 green = yellowUpRight;
    green     += textureLodOffset(src, uv, lod, ivec2( 2,  2)).rgb;
    green     += greenDownRight;
    green     += center;

    vec3 blue = center;
    blue     += greenDownRight;
    blue     += textureLodOffset(src, uv, lod, ivec2( 2, -2)).rgb;
    blue     += blueDownLeft;

    vec3 lila  = yellowDownLeft;
    lila      += center;
    lila      += blueDownLeft;
    lila      += textureLodOffset(src, uv, lod, ivec2(-2, -2)).rgb;

    vec3 red = textureLodOffset(src, uv, lod, ivec2(-1,  1)).rgb;
    red     += textureLodOffset(src, uv, lod, ivec2( 1,  1)).rgb;
    red     += textureLodOffset(src, uv, lod, ivec2( 1, -1)).rgb;
    red     += textureLodOffset(src, uv, lod, ivec2(-1, -1)).rgb;

    return (red * 0.5 + (yellow + green + blue + lila) * 0.125) * 0.25;
}

vec3 Upsample(sampler2D src, vec2 uv, float lod)
{
    vec3 result = textureLodOffset(src, uv, lod, ivec2(-1.0, 1.0)).rgb * 1.0;
    result     += textureLodOffset(src, uv, lod, ivec2( 0.0, 1.0)).rgb * 2.0;
    result     += textureLodOffset(src, uv, lod, ivec2( 1.0, 1.0)).rgb * 1.0;

    result     += textureLodOffset(src, uv, lod, ivec2(-1.0, 0.0)).rgb * 2.0;
    result     += textureLod(src, uv, lod).rgb * 4.0;
    result     += textureLodOffset(src, uv, lod, ivec2( 1.0, 0.0)).rgb * 2.0;
    
    result     += textureLodOffset(src, uv, lod, ivec2(-1.0, -1.0)).rgb * 1.0;
    result     += textureLodOffset(src, uv, lod, ivec2( 0.0, -1.0)).rgb * 2.0;
    result     += textureLodOffset(src, uv, lod, ivec2( 1.0, -1.0)).rgb * 1.0;

    return result / 16.0;
}

vec3 Prefilter(vec3 color, float maxColor, float threshold)
{
    const float Knee = 0.2;
    color = min(vec3(maxColor), color);

    float brightness = max(max(color.r, color.g), color.b);

    vec3 curve = vec3(threshold - Knee, Knee * 2.0, 0.25 / Knee);
    float rq = clamp(brightness - curve.x, 0.0, curve.y);
    rq = (rq * rq) * curve.z;
    color *= max(rq, brightness - threshold) / max(brightness, 0.0001);
    
    return color;
}
