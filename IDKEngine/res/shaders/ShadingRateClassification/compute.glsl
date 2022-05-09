#version 460 core
#define SHADING_RATE_1_INVOCATION_PER_PIXEL_NV 0u
#define SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV 1u
#define SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV 2u
#define SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV 3u
#define SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV 4u
#define MESHES_CLEAR_COLOR -1.0
#define TILE_SIZE 16
#define DEBUG_NO_DEBUG 0
#define DEBUG_SHADING_RATES 1
#define DEBUG_ABS_VELOCITY 2
#define DEBUG_LUMINANCE 3
#define DEBUG_LUMINANCE_VARIANCE 4
layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0, r8ui) restrict writeonly uniform uimage2D ImgResult;
layout(binding = 1, rgba16f) restrict uniform image2D ImgShaded;
layout(binding = 0) uniform sampler2D SamplerVelocity;
layout(binding = 1) uniform isampler2D SamplerMeshes;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FreezeFramesCounter;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
} basicDataUBO;

layout(std140, binding = 5) uniform TaaDataUBO
{
    vec4 Jitters[36 / 2];
    int Samples;
    int Enabled;
    int Frame;
    float VelScale;
} taaDataUBO;

float GetLuminance(vec3 color);

uniform int DebugMode;
uniform float Aggressiveness;

shared uint SharedDebugShadingRate;
shared vec2 SharedMeanVelocity[TILE_SIZE * TILE_SIZE];
shared float SharedMeanLum[TILE_SIZE * TILE_SIZE];
shared float SharedMeanLumVariance[TILE_SIZE * TILE_SIZE];

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);
const float VARIANCE_AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE - 1);
void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgShaded);

    vec2 vel = (texture(SamplerVelocity, uv).rg / taaDataUBO.VelScale) * AVG_MULTIPLIER;
    SharedMeanVelocity[gl_LocalInvocationIndex] = abs(vel);

    vec3 srcColor = imageLoad(ImgShaded, imgCoord).rgb;
    float luminance = GetLuminance(srcColor);
    SharedMeanLum[gl_LocalInvocationIndex] = luminance * AVG_MULTIPLIER;

    barrier();

    // Use parallel reduction to calculate sum (average) of all velocity and luminance values
    // Final results will have been collapsed into the first array element
    for (int cutoff = (TILE_SIZE * TILE_SIZE) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedMeanVelocity[gl_LocalInvocationIndex] += SharedMeanVelocity[cutoff + gl_LocalInvocationIndex];
            SharedMeanLum[gl_LocalInvocationIndex] += SharedMeanLum[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }

    float lumDiffToMean = luminance - SharedMeanLum[0];
    SharedMeanLumVariance[gl_LocalInvocationIndex] = pow(lumDiffToMean, 2.0) * VARIANCE_AVG_MULTIPLIER;
    barrier();

    // Use parallel reduction to calculate sum (average) of squared luminance deltas - variance
    // Final luminance variance will have been collapsed into the first array element
    for (int cutoff = (TILE_SIZE * TILE_SIZE) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedMeanLumVariance[gl_LocalInvocationIndex] += SharedMeanLumVariance[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }

    if (gl_LocalInvocationIndex == 0)
    {
        vec2 avgVelocity = SharedMeanVelocity[0] / basicDataUBO.DeltaUpdate;
        float maxAvgVelocity = max(avgVelocity.x, avgVelocity.y);
        // higher variance -> select higher shading rate (higher res)
        float scaledMeanLumVariance = SharedMeanLumVariance[0] * 600000.0;

        float velocityShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, maxAvgVelocity * Aggressiveness);
        float varianceShadingRate = mix(SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV, Aggressiveness / scaledMeanLumVariance);
        
        float combinedShadingRate = velocityShadingRate + varianceShadingRate;
        uint finalShadingRate = clamp(uint(round(combinedShadingRate)), SHADING_RATE_1_INVOCATION_PER_PIXEL_NV, SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV);

        if (DebugMode != DEBUG_NO_DEBUG)
            SharedDebugShadingRate = finalShadingRate;

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(finalShadingRate));
    }

    if (DebugMode != DEBUG_NO_DEBUG)
    {
        barrier();
        
        vec3 debugcolor = srcColor;
        int meshID = texture(SamplerMeshes, uv).r;
        // Don't show shading rate for non meshes like a skybox
        // since those get rendered differently and are not effected
        if (meshID != MESHES_CLEAR_COLOR)
        {
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
                debugcolor = vec3(SharedMeanLumVariance[0]);
            }
            else if (DebugMode == DEBUG_ABS_VELOCITY)
            {
                debugcolor = vec3(SharedMeanVelocity[0] / basicDataUBO.DeltaUpdate, 0.0);
            }
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