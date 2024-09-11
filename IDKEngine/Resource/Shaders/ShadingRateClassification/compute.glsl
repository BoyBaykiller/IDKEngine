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
    ENUM_DEBUG_MODE DebugMode;
    float SpeedFactor;
    float LumVarianceFactor;
} settingsUBO;

void GetTileData(vec3 color, vec2 velocity, out float summedSpeed, out float summedLuminance, out float summedLuminanceSquared);
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

const uint SAMPLES_PER_TILE = TILE_SIZE * TILE_SIZE;

shared float SharedSummedSpeed[SAMPLES_PER_TILE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedSummedLum[SAMPLES_PER_TILE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedSummedLumSquared[SAMPLES_PER_TILE / MIN_EFFECTIVE_SUBGROUP_SIZE];

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / textureSize(SamplerShaded, 0);

    vec2 velocity = texelFetch(gBufferDataUBO.Velocity, imgCoord, 0).rg;
    vec3 srcColor = texelFetch(SamplerShaded, imgCoord, 0).rgb;

    float summedSpeed, summedLuminance, summedLuminanceSquared;
    GetTileData(srcColor, velocity, summedSpeed, summedLuminance, summedLuminanceSquared);

    if (gl_LocalInvocationIndex == 0)
    {
        float meanSpeed = summedSpeed / SAMPLES_PER_TILE;
        meanSpeed /= perFrameDataUBO.DeltaRenderTime;
                
        float meanLuminance = summedLuminance / SAMPLES_PER_TILE;

        ENUM_SHADING_RATE finalShadingRate;
        float coeffOfVariation;
        if (meanLuminance <= 0.001)
        {
            finalShadingRate = ENUM_SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV;
            coeffOfVariation = 0.0;
        }
        else
        {
            // https://blog.demofox.org/2020/03/10/how-do-i-calculate-variance-in-1-pass/
            // https://en.wikipedia.org/wiki/Coefficient_of_variation
            float meanLuminanceSquared = summedLuminanceSquared / SAMPLES_PER_TILE;
            float variance = meanLuminanceSquared - meanLuminance * meanLuminance;
            float stdDev = sqrt(variance);
            coeffOfVariation = stdDev / meanLuminance;

            float velocityShadingRate = mix(ENUM_SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, ENUM_SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, meanSpeed * settingsUBO.SpeedFactor);
            float varianceShadingRate = mix(ENUM_SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, ENUM_SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, settingsUBO.LumVarianceFactor / coeffOfVariation);
            
            float combinedShadingRate = velocityShadingRate + varianceShadingRate;
            finalShadingRate = clamp(uint(round(combinedShadingRate)), ENUM_SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, ENUM_SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV);
        }

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(finalShadingRate));

        if (settingsUBO.DebugMode == ENUM_DEBUG_MODE_SPEED)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(meanSpeed));
        }
        else if (settingsUBO.DebugMode == ENUM_DEBUG_MODE_LUMINANCE)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(meanLuminance));
        }
        else if (settingsUBO.DebugMode == ENUM_DEBUG_MODE_LUMINANCE_VARIANCE)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(coeffOfVariation));
        }
    }
}

void GetTileData(vec3 color, vec2 velocity, out float summedSpeed, out float summedLuminance, out float summedLuminanceSquared)
{
    #if GL_KHR_shader_subgroup_arithmetic
    uint effectiveSubgroupSize = gl_SubgroupSize;
    #else
    uint effectiveSubgroupSize = 1; // effectively 1 if we can't use subgroup arithmetic
    #endif
    float luminance = GetLuminance(color);

    #if GL_KHR_shader_subgroup_arithmetic
    float subgroupAddedSpeed = subgroupAdd(length(velocity));
    float subgroupAddedLum = subgroupAdd(luminance);
    float subgroupAddedSquaredLum = subgroupAdd(luminance * luminance);

    if (subgroupElect())
    {
        SharedSummedSpeed[gl_SubgroupID] = subgroupAddedSpeed;
        SharedSummedLum[gl_SubgroupID] = subgroupAddedLum;
        SharedSummedLumSquared[gl_SubgroupID] = subgroupAddedSquaredLum;
    }
    #else
    SharedSummedSpeed[gl_LocalInvocationIndex] = length(velocity);
    SharedSummedLum[gl_LocalInvocationIndex] = luminance;
    SharedSummedLumSquared[gl_LocalInvocationIndex] = luminance * luminance;
    #endif
    barrier();

    // Use parallel reduction to calculate sum (average) of all velocity and luminance values
    // Final results will have been collapsed into the first array element
    for (uint cutoff = (SAMPLES_PER_TILE / effectiveSubgroupSize) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedSummedSpeed[gl_LocalInvocationIndex] += SharedSummedSpeed[cutoff + gl_LocalInvocationIndex];
            SharedSummedLum[gl_LocalInvocationIndex] += SharedSummedLum[cutoff + gl_LocalInvocationIndex];
            SharedSummedLumSquared[gl_LocalInvocationIndex] += SharedSummedLumSquared[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }

    summedSpeed = SharedSummedSpeed[0];
    summedLuminance = SharedSummedLum[0];
    summedLuminanceSquared = SharedSummedLumSquared[0];
}

float GetLuminance(vec3 color)
{
    return (color.x + color.y + color.z) * (1.0 / 3.0);
}