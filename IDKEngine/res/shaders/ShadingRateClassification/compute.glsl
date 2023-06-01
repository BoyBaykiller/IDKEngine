#version 460 core
#extension GL_ARB_bindless_texture : require
#extension GL_KHR_shader_subgroup_arithmetic : enable
#extension GL_NV_gpu_shader5 : enable
#extension GL_AMD_gcn_shader : enable

AppInclude(ShadingRateClassification/include/Constants.glsl)

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform uimage2D ImgResult;
layout(binding = 1) restrict writeonly uniform image2D ImgDebug;
layout(binding = 0) uniform sampler2D SamplerShaded;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    float _pad0;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    int Enabled;
    uint Frame;
    float VelScale;
} taaDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

void GetTileData(vec3 color, vec2 velocity, out float meanSpeed, out float meanLuminance, out float luminanceVariance);
float GetLuminance(vec3 color);

uniform float SpeedFactor;
uniform float LumVarianceFactor;
uniform int DebugMode;

#if !defined GL_KHR_shader_subgroup_arithmetic
#define MIN_EFFECTIVE_SUBGROUP_SIZE 1 // effectively 1 if we can't use subgroup arithmetic
#elif GL_NV_gpu_shader5
#define MIN_EFFECTIVE_SUBGROUP_SIZE 32 // NVIDIA device (fixed subgroup size)
#elif GL_AMD_gcn_shader
#define MIN_EFFECTIVE_SUBGROUP_SIZE 32 // AMD device (smallest possible subgroup size)
#else
#define MIN_EFFECTIVE_SUBGROUP_SIZE 8 // worst case on anything else
#endif

shared float SharedMeanSpeed[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedMeanLum[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedLuminanceVariance[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);
const float VARIANCE_AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE - 1);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / textureSize(SamplerShaded, 0);

    vec2 velocity = texture(gBufferDataUBO.Velocity, uv).rg / taaDataUBO.VelScale;
    vec3 srcColor = texture(SamplerShaded, uv).rgb;

    float meanSpeed, meanLuminance, luminanceVariance;
    GetTileData(srcColor, velocity, meanSpeed, meanLuminance, luminanceVariance);

    if (gl_LocalInvocationIndex == 0)
    {
        meanSpeed /= basicDataUBO.DeltaUpdate;
                
        uint finalShadingRate;
        float normalizedVariance;
        if (meanLuminance <= 0.001)
        {
            finalShadingRate = SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV;
            normalizedVariance = 0.0;
        }
        else
        {
            // Source: https://www.vosesoftware.com/riskwiki/Normalizedmeasuresofspread-theCofV.php
            float stdDev = sqrt(luminanceVariance);
            normalizedVariance = stdDev / meanLuminance;

            float velocityShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, meanSpeed * SpeedFactor);
            float varianceShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, LumVarianceFactor / normalizedVariance);
            
            float combinedShadingRate = velocityShadingRate + varianceShadingRate;
            finalShadingRate = clamp(uint(round(combinedShadingRate)), SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV);
        }

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(finalShadingRate));

        if (DebugMode == DEBUG_MODE_SPEED)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(meanSpeed));
        }
        else if (DebugMode == DEBUG_MODE_LUMINANCE)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(meanLuminance));
        }
        else if (DebugMode == DEBUG_MODE_LUMINANCE_VARIANCE)
        {
            imageStore(ImgDebug, ivec2(gl_WorkGroupID.xy), vec4(normalizedVariance));
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
    float subgroupAddedDeltaLumMean = subgroupAdd(pow(deltaLumMean, 2.0) * VARIANCE_AVG_MULTIPLIER);
    if (subgroupElect())
    {
        SharedLuminanceVariance[gl_SubgroupID] = subgroupAddedDeltaLumMean;
    }
    #else
    SharedLuminanceVariance[gl_LocalInvocationIndex] = pow(deltaLumMean, 2.0) * VARIANCE_AVG_MULTIPLIER;
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