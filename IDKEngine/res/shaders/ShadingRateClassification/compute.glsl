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

// Could also implement subgroup optimizations for AMD
// using AMD_shader_ballot and ARB_shader_ballot but useless since
// NV_shading_rate_image is only available on recent NVIDIA cards

#extension GL_KHR_shader_subgroup_arithmetic : enable

// Inserted by application.
// Will be 1 if no subgroup features are available
#define EFFECTIVE_SUBGROUP_SIZE __effectiveSubroupSize__

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
    int FreezeFrameCounter;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
} basicDataUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    int Frame;
    float VelScale;
} taaDataUBO;

float GetLuminance(vec3 color);

uniform int DebugMode;
uniform float SpeedFactor;
uniform float LumVarianceFactor;

shared uint SharedDebugShadingRate;

shared float SharedMeanSpeed[TILE_SIZE * TILE_SIZE / EFFECTIVE_SUBGROUP_SIZE];
shared float SharedMeanLum[TILE_SIZE * TILE_SIZE / EFFECTIVE_SUBGROUP_SIZE];
shared float SharedMeanLumVariance[TILE_SIZE * TILE_SIZE / EFFECTIVE_SUBGROUP_SIZE];

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE / EFFECTIVE_SUBGROUP_SIZE);
const float VARIANCE_AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE / EFFECTIVE_SUBGROUP_SIZE - 1);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgShaded);

    vec2 vel = texture(SamplerVelocity, uv).rg / taaDataUBO.VelScale;
    vec3 srcColor = imageLoad(ImgShaded, imgCoord).rgb;
    float luminance = GetLuminance(srcColor);
#if EFFECTIVE_SUBGROUP_SIZE == 1
    SharedMeanSpeed[gl_LocalInvocationIndex] = dot(vel, vel) * AVG_MULTIPLIER;
    SharedMeanLum[gl_LocalInvocationIndex] = luminance * AVG_MULTIPLIER;
#else
    float subgroupAddedSpeed = subgroupInclusiveAdd(dot(vel, vel) * AVG_MULTIPLIER);
    float subgroupAddedLum = subgroupInclusiveAdd(luminance * AVG_MULTIPLIER);

    if (subgroupElect())
    {
        SharedMeanSpeed[gl_SubgroupID] = subgroupAddedSpeed;
        SharedMeanLum[gl_SubgroupID] = subgroupAddedLum;
    }
#endif
    barrier();

    // Use parallel reduction to calculate sum (average) of all velocity and luminance values
    // Final results will have been collapsed into the first array element
    for (int cutoff = (TILE_SIZE * TILE_SIZE / EFFECTIVE_SUBGROUP_SIZE) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedMeanSpeed[gl_LocalInvocationIndex] += SharedMeanSpeed[cutoff + gl_LocalInvocationIndex];
            SharedMeanLum[gl_LocalInvocationIndex] += SharedMeanLum[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }

    float deltaLumMean = luminance - SharedMeanLum[0];
#if EFFECTIVE_SUBGROUP_SIZE == 1
    SharedMeanLumVariance[gl_LocalInvocationIndex] = pow(deltaLumMean, 2.0) * VARIANCE_AVG_MULTIPLIER;
#else
    float subgroupAddedDeltaLumMean = subgroupInclusiveAdd(pow(deltaLumMean, 2.0) * VARIANCE_AVG_MULTIPLIER);

    if (subgroupElect())
    {
        SharedMeanLumVariance[gl_SubgroupID] = subgroupAddedDeltaLumMean;
    }
#endif
    barrier();

    // Use parallel reduction to calculate sum (average) of squared luminance deltas - variance
    // Final luminance variance will have been collapsed into the first array element
    for (int cutoff = (TILE_SIZE * TILE_SIZE / EFFECTIVE_SUBGROUP_SIZE) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedMeanLumVariance[gl_LocalInvocationIndex] += SharedMeanLumVariance[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }

    if (gl_LocalInvocationIndex == 0)
    {
        float meanSpeed = sqrt(SharedMeanSpeed[0]) / basicDataUBO.DeltaUpdate;
        
        // Source: https://www.vosesoftware.com/riskwiki/Normalizedmeasuresofspread-theCofV.php
        float stdDev = sqrt(SharedMeanLumVariance[0]);
        float normalizedVariance = stdDev / SharedMeanLum[0];

        float velocityShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, meanSpeed * SpeedFactor);
        float varianceShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, LumVarianceFactor / normalizedVariance);
        
        float combinedShadingRate = velocityShadingRate + varianceShadingRate;
        uint finalShadingRate = clamp(uint(round(combinedShadingRate)), SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV);

        if (DebugMode == DEBUG_SHADING_RATES)
            SharedDebugShadingRate = finalShadingRate;

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(finalShadingRate));
    }

    if (DebugMode != DEBUG_NO_DEBUG)
    {
        barrier();
        
        vec3 debugcolor = srcColor;
        if (DebugMode == DEBUG_SHADING_RATES)
        {
            if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV)
                debugcolor += vec3(4, 0, 0);
            else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV)
                debugcolor += vec3(4, 4, 0);
            else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV)
                debugcolor += vec3(0, 4, 0);
            else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV)
                debugcolor += vec3(0, 0, 4);

        }
        else if (DebugMode == DEBUG_LUMINANCE)
        {
            debugcolor = vec3(SharedMeanLum[0]);
        }
        else if (DebugMode == DEBUG_LUMINANCE_VARIANCE)
        {
            float stdDev = sqrt(SharedMeanLumVariance[0]);
            float normalizedVariance = stdDev / SharedMeanLum[0];
            // darken a bit for debug view
            debugcolor = vec3(normalizedVariance * 0.2);
        }
        else if (DebugMode == DEBUG_SPEED)
        {
            debugcolor = vec3(sqrt(SharedMeanSpeed[0]) / basicDataUBO.DeltaUpdate);
        }


        if (gl_LocalInvocationID.x * gl_LocalInvocationID.y == 0)
        {
            debugcolor = vec3(0.0);
        }
        
        imageStore(ImgShaded, imgCoord, vec4(debugcolor, 1.0));
    }
}

float GetLuminance(vec3 color)
{
    return (color.x + color.y + color.z) * (1.0 / 3.0);
}