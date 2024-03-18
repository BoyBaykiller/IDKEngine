#version 460 core

AppInclude(PathTracing/include/Constants.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba32f) restrict uniform image2D ImgResult;

struct HitInfo
{
    vec3 Bary;
    float T;
    uvec3 VertexIndices;
    uint InstanceID;
};

struct WavefrontRay
{
    vec3 Origin;
    float PreviousIOROrDebugNodeCounter;

    vec3 Throughput;
    float CompressedDirectionX;

    vec3 Radiance;
    float CompressedDirectionY;
};

struct DispatchCommand
{
    int NumGroupsX;
    int NumGroupsY;
    int NumGroupsZ;
};

layout(std430, binding = 8) restrict readonly buffer WavefrontRaySSBO
{
    WavefrontRay Rays[];
} wavefrontRaySSBO;

layout(std430, binding = 9) restrict buffer WavefrontPTSSBO
{
    DispatchCommand DispatchCommand;
    uint Counts[2];
    uint PingPongIndex;
    uint AccumulatedSamples;
    uint AliveRayIndices[];
} wavefrontPTSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    uint Frame;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaRenderTime;
    float Time;
} basicDataUBO;

vec3 SpectralJet(float a);

uniform bool IsDebugBVHTraversal;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;
    WavefrontRay wavefrontRay = wavefrontRaySSBO.Rays[rayIndex];

    vec3 irradiance = wavefrontRay.Radiance;
    if (IsDebugBVHTraversal)
    {
        // use visible light spectrum as heatmap
        float a = min(wavefrontRay.PreviousIOROrDebugNodeCounter / 150.0, 1.0);
        vec3 col = SpectralJet(a);
        irradiance = col;
    }

    vec3 lastFrameIrradiance = imageLoad(ImgResult, imgCoord).rgb;
    irradiance = mix(lastFrameIrradiance, irradiance, 1.0 / (float(wavefrontPTSSBO.AccumulatedSamples) + 1.0));
    imageStore(ImgResult, imgCoord, vec4(irradiance, 1.0));

    // Reset global memory for next frame
    if (gl_GlobalInvocationID.x == 0)
    {
        wavefrontPTSSBO.DispatchCommand.NumGroupsX = 0;
        wavefrontPTSSBO.DispatchCommand.NumGroupsY = 1;
        wavefrontPTSSBO.DispatchCommand.NumGroupsZ = 1;
        
        wavefrontPTSSBO.Counts[0] = 0u;
        wavefrontPTSSBO.Counts[1] = 0u;
    }
}

vec3 SpectralJet(float a)
{
    // Source: https://www.shadertoy.com/view/ls2Bz1
    
    vec3 c;
    if (a < 0.25)
    {
        c = vec3(0.0, 4.0 * a, 1.0);
    }
    else if (a < 0.5)
    {
        c = vec3(0.0, 1.0, 1.0 + 4.0 * (0.25 - a));
    }
    else if (a < 0.75)
    {
        c = vec3(4.0 * (a - 0.5), 1.0, 0.0);
    }
    else
    {
        c = vec3(1.0, 1.0 + 4.0 * (0.75 - a), 0.0);
    }
    return clamp(c, vec3(0.0), vec3(1.0));
}