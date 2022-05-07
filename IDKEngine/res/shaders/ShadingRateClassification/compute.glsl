#version 460 core
#define SHADING_RATE_1_INVOCATION_PER_PIXEL_NV 0u
#define SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV 1u
#define SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV 2u
#define SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV 3u
#define SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV 4u

#define TILE_SIZE 16
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

uniform bool IsDebug;
uniform float Aggressiveness;

shared uint SharedDebugShadingRate;
shared vec2 SharedVelocity[TILE_SIZE * TILE_SIZE];

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);
void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / textureSize(SamplerVelocity, 0);

    vec2 vel = (texture(SamplerVelocity, uv).rg / taaDataUBO.VelScale) * AVG_MULTIPLIER;
    SharedVelocity[gl_LocalInvocationIndex] = abs(vel * Aggressiveness);
    barrier();

    // Use parallel reduction to calculate sum (average) of all velocities
    for (int cutoff = (TILE_SIZE * TILE_SIZE) / 2; cutoff > 0; cutoff /= 2)
    {
        if (gl_LocalInvocationIndex < cutoff)
        {
            SharedVelocity[gl_LocalInvocationIndex] += SharedVelocity[cutoff + gl_LocalInvocationIndex];
        }
        barrier();
    }

    if (gl_LocalInvocationIndex == 0)
    {
        vec2 avgVelocity = SharedVelocity[0] / basicDataUBO.DeltaUpdate;
        float maxAvgVelocity = max(avgVelocity.x, avgVelocity.y);
        uint rate = SHADING_RATE_1_INVOCATION_PER_PIXEL_NV;
        if (maxAvgVelocity > 1.0)
        {
            rate = SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV;
        }
        else if (maxAvgVelocity > 0.1)
        {
            rate = SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV;
        }
        else if (maxAvgVelocity > 0.05)
        {
            rate = SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV;
        }
        else if (maxAvgVelocity > 0.01)
        {
            rate = SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV;
        }

        if (IsDebug)
            SharedDebugShadingRate = rate;

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(rate));
    }

    if (IsDebug && imgCoord.x < imageSize(ImgShaded).x / 2)
    {
        barrier();
        
        vec3 debugcolor = vec3(0.0);
        if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV)
            debugcolor = vec3(4, 0, 0);
        else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV)
            debugcolor = vec3(4, 4, 0);
        else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV)
            debugcolor = vec3(0, 4, 0);
        else if (SharedDebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV)
            debugcolor = vec3(0, 0, 4);

        vec3 current = imageLoad(ImgShaded, imgCoord).rgb;
        debugcolor += current;

        if (gl_LocalInvocationID.x == 0 || gl_LocalInvocationID.y == 0)
        {
            debugcolor = vec3(0.0);
        }
        
        imageStore(ImgShaded, imgCoord, vec4(debugcolor, 1.0));
    }
}