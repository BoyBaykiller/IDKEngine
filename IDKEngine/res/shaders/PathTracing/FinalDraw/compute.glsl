#version 460 core

AppInclude(PathTracing/include/Constants.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba32f) restrict uniform image2D ImgResult;

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
    uint NumGroupsX;
    uint NumGroupsY;
    uint NumGroupsZ;
};

layout(std430, binding = 8) restrict readonly buffer WavefrontRaySSBO
{
    WavefrontRay Rays[];
} wavefrontRaySSBO;

layout(std430, binding = 9) restrict buffer WavefrontPTSSBO
{
    DispatchCommand DispatchCommands[2];
    uint Counts[2];
    uint AccumulatedSamples;
    uint Indices[];
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
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    // Reset global memory for next frame
    if (gl_GlobalInvocationID.x == 0)
    {
        wavefrontPTSSBO.DispatchCommands[0].NumGroupsX = 0u;
        wavefrontPTSSBO.DispatchCommands[0].NumGroupsY = 1u;
        wavefrontPTSSBO.DispatchCommands[0].NumGroupsZ = 1u;

        wavefrontPTSSBO.DispatchCommands[1].NumGroupsX = 0u;
        wavefrontPTSSBO.DispatchCommands[1].NumGroupsY = 1u;
        wavefrontPTSSBO.DispatchCommands[1].NumGroupsZ = 1u;
        
        wavefrontPTSSBO.Counts[0] = 0u;
        wavefrontPTSSBO.Counts[1] = 0u;
    }

    uint rayIndex = imgCoord.y * imgResultSize.x + imgCoord.x;
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
}

// Source: https://www.shadertoy.com/view/ls2Bz1
vec3 SpectralJet(float a)
{
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