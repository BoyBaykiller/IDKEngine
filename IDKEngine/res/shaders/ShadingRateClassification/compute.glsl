#version 460 core
#define SHADING_RATE_1_INVOCATION_PER_PIXEL_NV 0u
#define SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV 1u
#define SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV 2u
#define SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV 3u
#define SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV 4u
#define TILE_SIZE 16
#define DEBUG_NO_DEBUG 0
#define DEBUG_SHADING_RATES 1
#define DEBUG_SPEED 2
#define DEBUG_LUMINANCE 3
#define DEBUG_LUMINANCE_VARIANCE 4
#extension GL_KHR_shader_subgroup_arithmetic : enable

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0, r8ui) restrict writeonly uniform uimage2D ImgResult;
layout(binding = 1, rgba16f) restrict uniform image2D ImgShaded;
layout(binding = 0) uniform sampler2D SamplerVelocity;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
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
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    uint Frame;
    float VelScale;
} taaDataUBO;

void GetTileData(vec3 color, vec2 velocity, out float meanSpeed, out float meanLuminance, out float luminanceVariance);
float GetLuminance(vec3 color);

uniform int DebugMode;
uniform float SpeedFactor;
uniform float LumVarianceFactor;

#ifndef GL_KHR_shader_subgroup_arithmetic
#define MIN_EFFECTIVE_SUBGROUP_SIZE 1 // effectively 1 if we can't use subgroup arithmetic
#elif GL_NV_gpu_shader5
#define MIN_EFFECTIVE_SUBGROUP_SIZE 32 // nvidia device
#else
#define MIN_EFFECTIVE_SUBGROUP_SIZE 8 // worst case on Intel hardware
#endif

shared float SharedMeanSpeed[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedMeanLum[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];
shared float SharedLuminanceVariance[TILE_SIZE * TILE_SIZE / MIN_EFFECTIVE_SUBGROUP_SIZE];

shared uint SharedDebugShadingRate;
shared float SharedDebugNormalizedVariance;

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);
const float VARIANCE_AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE - 1);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgShaded);

    vec2 velocity = texture(SamplerVelocity, uv).rg / taaDataUBO.VelScale;
    vec3 srcColor = imageLoad(ImgShaded, imgCoord).rgb;

    float meanSpeed, meanLuminance, luminanceVariance;
    GetTileData(srcColor, velocity, meanSpeed, meanLuminance, luminanceVariance);

    if (gl_LocalInvocationIndex == 0)
    {
        meanSpeed /= basicDataUBO.DeltaUpdate;
        
        // Source: https://www.vosesoftware.com/riskwiki/Normalizedmeasuresofspread-theCofV.php
        float stdDev = sqrt(luminanceVariance);
        float normalizedVariance = stdDev / meanLuminance;

        float velocityShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, meanSpeed);
        float varianceShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, LumVarianceFactor / normalizedVariance);
        
        float combinedShadingRate = velocityShadingRate + varianceShadingRate;
        uint finalShadingRate = clamp(uint(round(combinedShadingRate)), SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV);

        if (DebugMode == DEBUG_SHADING_RATES)
        {
            SharedDebugShadingRate = finalShadingRate;
        }
        else if (DebugMode == DEBUG_LUMINANCE_VARIANCE)
        {
            SharedDebugNormalizedVariance = normalizedVariance;
        }

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(finalShadingRate));
    }

    if (DebugMode != DEBUG_NO_DEBUG)
    {
        barrier();
        
        vec3 debugColor = srcColor;
        if (DebugMode == DEBUG_SHADING_RATES)
        {
            if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV)
                debugColor += vec3(4, 0, 0);
            else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV)
                debugColor += vec3(4, 4, 0);
            else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV)
                debugColor += vec3(0, 4, 0);
            else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV)
                debugColor += vec3(0, 0, 4);
        }
        else if (DebugMode == DEBUG_LUMINANCE)
        {
            debugColor = vec3(meanLuminance);
        }
        else if (DebugMode == DEBUG_LUMINANCE_VARIANCE)
        {
            debugColor = vec3(SharedDebugNormalizedVariance) * 0.2;
        }
        else if (DebugMode == DEBUG_SPEED)
        {
            debugColor = vec3(meanSpeed / basicDataUBO.DeltaUpdate);
        }

        if (gl_LocalInvocationID.x == 0 || gl_LocalInvocationID.y == 0)
        {
            debugColor = vec3(0.0);
        }
        
        imageStore(ImgShaded, imgCoord, vec4(debugColor, 1.0));
    }
}

void GetTileData(vec3 color, vec2 velocity, out float meanSpeed, out float meanLuminance, out float luminanceVariance)
{
    #ifdef GL_KHR_shader_subgroup_arithmetic
    uint effectiveSubgroupSize = gl_SubgroupSize;
    #else
    uint effectiveSubgroupSize = 1; // effectively 1 if we can't use subgroup arithmetic
    #endif
    float luminance = GetLuminance(color);

    #ifdef GL_KHR_shader_subgroup_arithmetic
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
    #ifdef GL_KHR_shader_subgroup_arithmetic
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