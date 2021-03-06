#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -3.4028235e+38

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerLastForwardPass;
layout(binding = 1) uniform sampler2D SamplerVelocity;
layout(binding = 2) uniform sampler2D SamplerDepth;

layout(std140, binding = 3) uniform TaaDataUBO
{
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    int Frame;
    float VelScale;
} taaDataUBO;

void GetResolveData(ivec2 imgCoord, out ivec2 bestVelocityPixel, out vec3 currentColor, out vec3 neighborhoodMin, out vec3 neighborhoodMax);
vec4 SampleTextureCatmullRom(sampler2D srcTexture, vec2 uv);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    ivec2 bestVelocityPixel;
    vec3 neighborhoodMin, neighborhoodMax;
    vec3 currentColor;
    GetResolveData(imgCoord, bestVelocityPixel, currentColor, neighborhoodMin, neighborhoodMax);

    vec2 velocity = texelFetch(SamplerVelocity, bestVelocityPixel, 0).rg / taaDataUBO.VelScale;
    vec2 oldUV = uv - velocity;
    if (any(greaterThan(oldUV, vec2(1.0))) || any(lessThan(oldUV, vec2(0.0))))
    {
        imageStore(ImgResult, imgCoord, vec4(currentColor, 1.0));
        return;
    }

    vec3 historyColor = SampleTextureCatmullRom(SamplerLastForwardPass, oldUV).rgb;
    historyColor = clamp(historyColor, neighborhoodMin, neighborhoodMax);

    float blend = 1.0 / taaDataUBO.Samples;

    // Further reduces blur caused by sampling with reprojected pixel that's not in pixel center 
    // Source: https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/temporalaaCS.hlsl#L121
    ivec2 size = imageSize(ImgResult);
    float subpixelCorrection = fract(max(abs(velocity.x) * size.x, abs(velocity.y) * size.y)) * 0.5;
    blend = mix(blend, 0.8, subpixelCorrection);

    vec3 color = mix(historyColor, currentColor, blend);
    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}

// 1. Return best velocity pixel in a 3x3 radius
// 2. Return min/max colors in a 3x3 radius
// 3. Return color of the current coords 
// Source: https://www.elopezr.com/temporal-aa-and-the-quest-for-the-holy-trail/
void GetResolveData(ivec2 imgCoord, out ivec2 bestVelocityPixel, out vec3 currentColor, out vec3 neighborhoodMin, out vec3 neighborhoodMax)
{
    float minDepth = 1.0;
    neighborhoodMin = vec3(FLOAT_MAX);
    neighborhoodMax = vec3(FLOAT_MIN);
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            ivec2 curPixel = imgCoord + ivec2(x, y);
    
            vec3 neighbor = imageLoad(ImgResult, curPixel).rgb;
            neighborhoodMin = min(neighborhoodMin, neighbor);
            neighborhoodMax = max(neighborhoodMax, neighbor);
            
            if (x == 0 && y == 0)
            {
                currentColor = neighbor;
            }
    
            float currentDepth = texelFetch(SamplerDepth, curPixel, 0).r;
            if (currentDepth < minDepth)
            {
                minDepth = currentDepth;
                bestVelocityPixel = curPixel;
            }
        }
    }
}

// This filter is better suited than the standard bilinear procedure
// as to not introducing too much of a blur
// Source: https://gist.github.com/TheRealMJP/c83b8c0f46b63f3a88a5986f4fa982b1
vec4 SampleTextureCatmullRom(sampler2D srcTexture, vec2 uv)
{
    vec2 texelSize = 1.0 / textureSize(srcTexture, 0);

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

    vec4 result = texture(srcTexture, vec2(texPos0.x, texPos0.y)) * w0.x * w0.y;
    result += texture(srcTexture, vec2(texPos12.x, texPos0.y)) * w12.x * w0.y;
    result += texture(srcTexture, vec2(texPos3.x, texPos0.y)) * w3.x * w0.y;

    result += texture(srcTexture, vec2(texPos0.x, texPos12.y)) * w0.x * w12.y;
    result += texture(srcTexture, vec2(texPos12.x, texPos12.y)) * w12.x * w12.y;
    result += texture(srcTexture, vec2(texPos3.x, texPos12.y)) * w3.x * w12.y;

    result += texture(srcTexture, vec2(texPos0.x, texPos3.y)) * w0.x * w3.y;
    result += texture(srcTexture, vec2(texPos12.x, texPos3.y)) * w12.x * w3.y;
    result += texture(srcTexture, vec2(texPos3.x, texPos3.y)) * w3.x * w3.y;

    return result;
}