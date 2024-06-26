#version 460 core
#extension GL_KHR_shader_subgroup_arithmetic : enable

AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(ShadingRateClassification/include/Constants.glsl)

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform uimage2D ImgResult;
layout(binding = 1) restrict writeonly uniform image2D ImgDebug;
layout(binding = 0) uniform sampler2D SamplerShaded;

layout(std140, binding = 7) uniform SettingsUBO
{
    int DebugMode;
    float SpeedFactor;
    float LumVarianceFactor;
} settingsUBO;

void GetTileData(vec3 color, vec2 velocity, out float meanSpeed, out float meanLuminance, out float luminanceVariance);
float GetLuminance(vec3 color);

#if !GL_KHR_shader_subgroup_arithmetic
    #define MIN_EFFECTIVE_SUBGROUP_SIZE 1 // Effectively 1 if we can't use subgroup arithmetic
#elif APP_VENDOR_NVIDIA
    #define MIN_EFFECTIVE_SUBGROUP_SIZE 32 // NVIDIA always has 32
#elif APP_VENDOR_AMD
    #define MIN_EFFECTIVE_SUBGROUP_SIZE 32 // AMD can run shaders in both wave32 or wave64 mode
#else
    #define MIN_EFFECTIVE_SUBGROUP_SIZE 8 // Intel can go as low as 8 (this is also for anything else) 
#endif

shared float SharedMeanSpeed[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedMeanLum[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedLuminanceVariance[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / textureSize(SamplerShaded, 0);

    vec2 velocity = texelFetch(gBufferDataUBO.Velocity, imgCoord, 0).rg;
    vec3 srcColor = texelFetch(SamplerShaded, imgCoord, 0).rgb;

    float meanSpeed, meanLuminance, luminanceVariance;
    GetTileData(srcColor, velocity, meanSpeed, meanLuminance, luminanceVariance);

    if (gl_LocalInvocationIndex == 0)
    {
        meanSpeed /= perFrameDataUBO.DeltaRenderTime;
                
        uint finalShadingRate;
        float coeffOfVariation;
        if (meanLuminance <= 0.001)
        {
            finalShadingRate = SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV;
            coeffOfVariation = 0.0;
        }
        else
        {
            // https://en.wikipedia.org/wiki/Coefficient_of_variation
            float stdDev = sqrt(luminanceVariance);
            coeffOfVariation = stdDev / meanLuminance;

            float velocityShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, meanSpeed * settingsUBO.SpeedFactor);
            float varianceShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, settingsUBO.LumVarianceFactor / coeffOfVariation);
            
            float combinedShadingRate = velocityShadingRate + varianceShadingRate;
            finalShadingRate = clamp(uint(round(combinedShadingRate)), SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV);
        }

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(finalShadingRate));

        if (settingsUBO.DebugMode == DEBUG_MODE_SPEED)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(meanSpeed));
        }
        else if (settingsUBO.DebugMode == DEBUG_MODE_LUMINANCE)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(meanLuminance));
        }
        else if (settingsUBO.DebugMode == DEBUG_MODE_LUMINANCE_VARIANCE)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(coeffOfVariation));
        }
    }
}

void GetTileData(vec3 color, vec2 velocity, out float meanSpeed, out float meanLuminance, out float luminanceVariance)
{
    #if GL_KHR_shader_subgroup_arithmetic
    uint effectiveSubgroupSize = gl_SubgroupSize;
    #else
    uint effectiveSubgroupSize = 1; // effectively 1 if we can't use subgroup arithmetic
    #endif
    float luminance = GetLuminance(color);

    #if GL_KHR_shader_subgroup_arithmetic
    float subgroupAddedSpeed = subgroupAdd(length(velocity) * AVG_MULTIPLIER);
    float subgroupAddedLum = subgroupAdd(luminance * AVG_MULTIPLIER);

    if (subgroupElect())
    {
        SharedMeanSpeed[gl_SubgroupID] = subgroupAddedSpeed;
        SharedMeanLum[gl_SubgroupID] = subgroupAddedLum;
    }
    #else
    SharedMeanSpeed[gl_LocalInvocationIndex] = length(velocity) * AVG_MULTIPLIER;
    SharedMeanLum[gl_LocalInvocationIndex] = luminance * AVG_MULTIPLIER;
    #endif
    barrier();

    // Use parallel reduction to calculate sum (average) of all velocity and luminance values
    // Final results will have been collapsed into the first array element
    for (uint cutoff = (TILE_SIZE * TILE_SIZE / effectiveSubgroupSize) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedMeanSpeed[gl_LocalInvocationIndex] += SharedMeanSpeed[cutoff + gl_LocalInvocationIndex];
            SharedMeanLum[gl_LocalInvocationIndex] += SharedMeanLum[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }
    meanSpeed = SharedMeanSpeed[0];
    meanLuminance = SharedMeanLum[0];

    float deltaLumMean = luminance - meanLuminance;
    #if GL_KHR_shader_subgroup_arithmetic
    float subgroupAddedDeltaLumMean = subgroupAdd(pow(deltaLumMean, 2.0) * AVG_MULTIPLIER);
    if (subgroupElect())
    {
        SharedLuminanceVariance[gl_SubgroupID] = subgroupAddedDeltaLumMean;
    }
    #else
    SharedLuminanceVariance[gl_LocalInvocationIndex] = pow(deltaLumMean, 2.0) * AVG_MULTIPLIER;
    #endif
    barrier();

    // Use parallel reduction to calculate sum (average) of squared luminance deltas - variance
    // Final luminance variance will have been collapsed into the first array element
    for (uint cutoff = (TILE_SIZE * TILE_SIZE / effectiveSubgroupSize) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedLuminanceVariance[gl_LocalInvocationIndex] += SharedLuminanceVariance[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }
    luminanceVariance = SharedLuminanceVariance[0];
}

float GetLuminance(vec3 color)
{
    return (color.x + color.y + color.z) * (1.0 / 3.0);
}