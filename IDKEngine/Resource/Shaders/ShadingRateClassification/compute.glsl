#version 460 core
#extension GL_KHR_shader_subgroup_arithmetic : require

AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(ShadingRateClassification/include/Constants.glsl)

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform uimage2D ImgResult;
layout(binding = 1) restrict writeonly uniform image2D ImgDebug;
layout(binding = 0) uniform sampler2D SamplerShaded;

layout(std140, binding = 0) uniform SettingsUBO
{
    ENUM_DEBUG_MODE DebugMode;
    float SpeedFactor;
    float LumVarianceFactor;
} settingsUBO;

void GetTileData(vec3 color, vec2 velocity, out float speedSum, out float luminanceSum, out float luminanceSquaredSum);
float GetLuminance(vec3 color);

const uint SAMPLES_PER_TILE = TILE_SIZE * TILE_SIZE;

shared float SharedSpeedSums[SAMPLES_PER_TILE / MIN_SUBGROUP_SIZE];
shared float SharedLumSums[SAMPLES_PER_TILE / MIN_SUBGROUP_SIZE];
shared float SharedLumSquaredSums[SAMPLES_PER_TILE / MIN_SUBGROUP_SIZE];

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / textureSize(SamplerShaded, 0);

    vec2 velocity = texelFetch(gBufferDataUBO.Velocity, imgCoord, 0).rg;
    vec3 srcColor = texelFetch(SamplerShaded, imgCoord, 0).rgb;

    float speedSum, luminanceSum, luminanceSquaredSum;
    GetTileData(srcColor, velocity, speedSum, luminanceSum, luminanceSquaredSum);

    if (gl_LocalInvocationIndex == 0)
    {
        float meanSpeed = speedSum / SAMPLES_PER_TILE;
        meanSpeed /= perFrameDataUBO.DeltaRenderTime;
                
        float luminanceMean = luminanceSum / SAMPLES_PER_TILE;

        ENUM_SHADING_RATE finalShadingRate;
        float coeffOfVariation;
        if (luminanceMean <= 0.001)
        {
            finalShadingRate = ENUM_SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV;
            coeffOfVariation = 0.0;
        }
        else
        {
            // https://blog.demofox.org/2020/03/10/how-do-i-calculate-variance-in-1-pass/
            // https://en.wikipedia.org/wiki/Coefficient_of_variation
            float luminanceSquaredMean = luminanceSquaredSum / SAMPLES_PER_TILE;
            float variance = luminanceSquaredMean - luminanceMean * luminanceMean;
            float stdDev = sqrt(variance);
            coeffOfVariation = stdDev / luminanceMean;

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
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(luminanceMean));
        }
        else if (settingsUBO.DebugMode == ENUM_DEBUG_MODE_LUMINANCE_VARIANCE)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(coeffOfVariation));
        }
    }
}

void GetTileData(vec3 color, vec2 velocity, out float speedSum, out float luminanceSum, out float luminanceSquaredSum)
{
    float luminance = GetLuminance(color);

    float subgroupAddedSpeed = subgroupAdd(length(velocity));
    float subgroupAddedLum = subgroupAdd(luminance);
    float subgroupAddedSquaredLum = subgroupAdd(luminance * luminance);

    if (subgroupElect())
    {
        SharedSpeedSums[gl_SubgroupID] = subgroupAddedSpeed;
        SharedLumSums[gl_SubgroupID] = subgroupAddedLum;
        SharedLumSquaredSums[gl_SubgroupID] = subgroupAddedSquaredLum;
    }

    barrier();

    if (gl_LocalInvocationIndex == 0)
    {
        for (int i = 1; i < gl_NumSubgroups; i++)
        {
            SharedSpeedSums[0] += SharedSpeedSums[i];
            SharedLumSums[0] += SharedLumSums[i];
            SharedLumSquaredSums[0] += SharedLumSquaredSums[i];
        }
    }
    barrier();

    speedSum = SharedSpeedSums[0];
    luminanceSum = SharedLumSums[0];
    luminanceSquaredSum = SharedLumSquaredSums[0];
}

float GetLuminance(vec3 color)
{
    return (color.x + color.y + color.z) * (1.0 / 3.0);
}