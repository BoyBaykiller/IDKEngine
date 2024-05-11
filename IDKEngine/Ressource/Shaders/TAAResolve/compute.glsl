#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX

AppInclude(include/StaticUniformBuffers.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerHistoryColor;
layout(binding = 1) uniform sampler2D SamplerCurrentColor;

void GetResolveData(ivec2 imgCoord, out vec4 currentColor, out vec2 neighborhoodBestUv, out vec4 neighborhoodMin, out vec4 neighborhoodMax);
vec4 SampleTextureCatmullRom(sampler2D src, vec2 uv);

uniform bool IsNaiveTaa;
uniform float PreferAliasingOverBlur;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    if (IsNaiveTaa)
    {
        vec2 velocity = texture(gBufferDataUBO.Velocity, uv).rg;
        vec2 historyUV = uv - velocity;

        vec3 currentColor = texture(SamplerCurrentColor, uv).rgb;
        vec3 historyColor = texture(SamplerHistoryColor, historyUV).rgb;

        float blend = 1.0 / taaDataUBO.SampleCount;

        vec3 color = mix(historyColor, currentColor, blend);
        imageStore(ImgResult, imgCoord, vec4(color, 1.0));
        return;
    }

    vec2 bestHistoryUv;
    vec4 currentColor, neighborhoodMin, neighborhoodMax;
    GetResolveData(imgCoord, currentColor, bestHistoryUv, neighborhoodMin, neighborhoodMax);

    vec2 velocity = texture(gBufferDataUBO.Velocity, bestHistoryUv).rg;
    vec2 historyUV = uv - velocity;
    if (any(greaterThanEqual(historyUV, vec2(1.0))) || any(lessThan(historyUV, vec2(0.0))))
    {
        imageStore(ImgResult, imgCoord, currentColor);
        return;
    }

    vec4 historyColor = SampleTextureCatmullRom(SamplerHistoryColor, historyUV);
    historyColor = clamp(historyColor, neighborhoodMin, neighborhoodMax);

    float blend = 1.0 / taaDataUBO.SampleCount;

    vec2 localPixelPos = fract(historyUV * textureSize(SamplerHistoryColor, 0));
    float pixelCenterDistance = abs(0.5 - localPixelPos.x) + abs(0.5 - localPixelPos.y);
    blend = mix(blend, 1.0, pixelCenterDistance * PreferAliasingOverBlur);
    
    vec4 color = mix(historyColor, currentColor, blend);
    imageStore(ImgResult, imgCoord, color);
}

// 1. Return color at the image coordinates 
// 2. Return best uv for accessing aliased history textures such as velocity
// 3. Return min/max colors in a 3x3 radius
void GetResolveData(ivec2 imgCoord, out vec4 currentColor, out vec2 neighborhoodBestUv, out vec4 neighborhoodMin, out vec4 neighborhoodMax)
{
    // Source: https://www.elopezr.com/temporal-aa-and-the-quest-for-the-holy-trail/
    
    float minDepth = FLOAT_MAX;
    neighborhoodMin = vec4(FLOAT_MAX);
    neighborhoodMax = vec4(FLOAT_MIN);
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            ivec2 curPixel = imgCoord + ivec2(x, y);
            vec2 uv = (curPixel + 0.5) / imageSize(ImgResult);

            vec4 neighborColor = texture(SamplerCurrentColor, uv);
            neighborhoodMin = min(neighborhoodMin, neighborColor);
            neighborhoodMax = max(neighborhoodMax, neighborColor);
            
            float inputDepth = texture(gBufferDataUBO.Depth, uv).r;
            if (inputDepth < minDepth)
            {
                minDepth = inputDepth;
                neighborhoodBestUv = uv;
            }

            if (x == 0 && y == 0)
            {
                currentColor = neighborColor;
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