#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX
#extension GL_ARB_bindless_texture : require

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerPrevResult;
layout(binding = 1) uniform sampler2D SamplerInputColor;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    int Frame;
    bool IsEnabled;
} taaDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

void GetResolveData(ivec2 imgCoord, out vec3 inputColor, out vec2 bestUv, out vec3 neighborhoodMin, out vec3 neighborhoodMax);
vec4 SampleTextureCatmullRom(sampler2D src, vec2 uv);

uniform bool IsTaaArtifactMitigation;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    if (!taaDataUBO.IsEnabled)
    {
        vec4 color = texture(SamplerInputColor, uv);
        imageStore(ImgResult, imgCoord, color);
        return;
    }

    if (!IsTaaArtifactMitigation)
    {
        vec2 velocity = texture(gBufferDataUBO.Velocity, uv).rg;
        vec2 historyUV = uv - velocity;

        vec3 inputColor = texture(SamplerInputColor, uv).rgb;
        vec3 historyColor = texture(SamplerPrevResult, historyUV).rgb;

        float blend = 1.0 / taaDataUBO.Samples;

        vec3 color = mix(historyColor, inputColor, blend);
        imageStore(ImgResult, imgCoord, vec4(color, 1.0));
        return;
    }

    vec2 bestUv;
    vec3 inputColor, neighborhoodMin, neighborhoodMax;
    GetResolveData(imgCoord, inputColor, bestUv, neighborhoodMin, neighborhoodMax);

    vec2 velocity = texture(gBufferDataUBO.Velocity, bestUv).rg;
    vec2 historyUV = uv - velocity;
    if (any(greaterThanEqual(historyUV, vec2(1.0))) || any(lessThan(historyUV, vec2(0.0))))
    {
        imageStore(ImgResult, imgCoord, vec4(inputColor, 1.0));
        return;
    }

    vec3 historyColor = SampleTextureCatmullRom(SamplerPrevResult, historyUV).rgb;
    historyColor = clamp(historyColor, neighborhoodMin, neighborhoodMax);

    float blend = 1.0 / taaDataUBO.Samples;

    // Further reduces blur caused by sampling with reprojected pixel that's not in pixel center 
    // Source: https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/temporalaaCS.hlsl#L121
    ivec2 size = imageSize(ImgResult);
    float subpixelCorrection = fract(max(abs(velocity.x) * size.x, abs(velocity.y) * size.y)) * 0.5;
    blend = mix(blend, 0.8, subpixelCorrection);

    vec3 color = mix(historyColor, inputColor, blend);
    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}

// 1. Return color at the image coordinates 
// 2. Return best velocity pixel in a 3x3 radius
// 3. Return min/max colors in a 3x3 radius
// Source: https://www.elopezr.com/temporal-aa-and-the-quest-for-the-holy-trail/
void GetResolveData(ivec2 imgCoord, out vec3 inputColor, out vec2 bestUv, out vec3 neighborhoodMin, out vec3 neighborhoodMax)
{
    float minDepth = FLOAT_MAX;
    neighborhoodMin = vec3(FLOAT_MAX);
    neighborhoodMax = vec3(FLOAT_MIN);
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            ivec2 curPixel = imgCoord + ivec2(x, y);
    
            vec2 uv = (curPixel + 0.5) / imageSize(ImgResult);
            vec3 neighborColor = texture(SamplerInputColor, uv).rgb;
            neighborhoodMin = min(neighborhoodMin, neighborColor);
            neighborhoodMax = max(neighborhoodMax, neighborColor);
            
            if (x == 0 && y == 0)
            {
                inputColor = neighborColor;
            }
    
            float inputDepth = texture(gBufferDataUBO.Depth, uv).r;
            if (inputDepth < minDepth)
            {
                minDepth = inputDepth;
                bestUv = uv;
            }
        }
    }
}

// This filter is better suited than the standard bilinear procedure
// as to not introducing too much of a blur
// Source: https://gist.github.com/TheRealMJP/c83b8c0f46b63f3a88a5986f4fa982b1
vec4 SampleTextureCatmullRom(sampler2D src, vec2 uv)
{
    vec2 texelSize = 1.0 / textureSize(src, 0);

    // We're going to sample a a 4x4 grid of texels surrounding the target UV coordinate. We'll do this by rounding
    // down the sample location to get the exact center of our "starting" texel. The starting texel will be at
    // location [1, 1] in the grid, where [0, 0] is the top left corner.
    vec2 samplePos = uv / texelSize;
    vec2 texPos1 = floor(samplePos - 0.5f) + 0.5f;

    // Compute the fractional offset from our starting texel to our original sample location, which we'll
    // feed into the Catmull-Rom spline function to get our filter weights.
    vec2 f = samplePos - texPos1;

    // Compute the Catmull-Rom weights using the fractional offset that we calculated earlier.
    // These equations are pre-expanded based on our knowledge of where the texels will be located,
    // which lets us avoid having to evaluate a piece-wise function.
    vec2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    vec2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    vec2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    vec2 w3 = f * f * (-0.5 + 0.5 * f);

    // Work out weighting factors and sampling offsets that will let us use bilinear filtering to
    // simultaneously evaluate the middle 2 samples from the 4x4 grid.
    vec2 w12 = w1 + w2;
    vec2 offset12 = w2 / (w1 + w2);

    // Compute the final UV coordinates we'll use for sampling the texture
    vec2 texPos0 = texPos1 - 1;
    vec2 texPos3 = texPos1 + 2;
    vec2 texPos12 = texPos1 + offset12;

    texPos0 *= texelSize;
    texPos3 *= texelSize;
    texPos12 *= texelSize;

    vec4 result = texture(src, vec2(texPos0.x, texPos0.y)) * w0.x * w0.y;
    result += texture(src, vec2(texPos12.x, texPos0.y)) * w12.x * w0.y;
    result += texture(src, vec2(texPos3.x, texPos0.y)) * w3.x * w0.y;

    result += texture(src, vec2(texPos0.x, texPos12.y)) * w0.x * w12.y;
    result += texture(src, vec2(texPos12.x, texPos12.y)) * w12.x * w12.y;
    result += texture(src, vec2(texPos3.x, texPos12.y)) * w3.x * w12.y;

    result += texture(src, vec2(texPos0.x, texPos3.y)) * w0.x * w3.y;
    result += texture(src, vec2(texPos12.x, texPos3.y)) * w12.x * w3.y;
    result += texture(src, vec2(texPos3.x, texPos3.y)) * w3.x * w3.y;

    return result;
}