#version 460 core

AppInclude(PathTracing/include/Constants.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba32f) restrict uniform image2D ImgResult;

struct TransportRay
{
    vec3 Origin;
    uint DebugNodeCounter;

    vec3 Direction;
    float PreviousIOR;

    vec3 Throughput;
    bool IsRefractive;

    vec3 Radiance;
    float _pad0;
};

struct DispatchCommand
{
    uint NumGroupsX;
    uint NumGroupsY;
    uint NumGroupsZ;
};

layout(std430, binding = 8) restrict readonly buffer TransportRaySSBO
{
    TransportRay Rays[];
} transportRaySSBO;

layout(std430, binding = 9) restrict buffer RayIndicesSSBO
{
    uint Counts[2];
    uint AccumulatedSamples;
    uint Indices[];
} rayIndicesSSBO;

layout(std430, binding = 10) restrict writeonly buffer DispatchCommandSSBO
{
    DispatchCommand DispatchCommands[2];
} dispatchCommandSSBO;

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
    float DeltaUpdate;
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
        uint maxPossibleRayCount = imgResultSize.x * imgResultSize.y;
        uint maxPossibleNumGroupsX = (maxPossibleRayCount + N_HIT_PROGRAM_LOCAL_SIZE_X - 1) / N_HIT_PROGRAM_LOCAL_SIZE_X;
        
        dispatchCommandSSBO.DispatchCommands[0].NumGroupsX = 0u;
        dispatchCommandSSBO.DispatchCommands[1].NumGroupsX = 0u;
        
        rayIndicesSSBO.Counts[0] = 0u;
        rayIndicesSSBO.Counts[1] = 0u;
    }

    uint rayIndex = imgCoord.y * imgResultSize.x + imgCoord.x;
    TransportRay transportRay = transportRaySSBO.Rays[rayIndex];

    vec3 irradiance = transportRay.Radiance;
    if (IsDebugBVHTraversal)
    {
        // use visible light spectrum as heatmap
        float a = min(transportRay.DebugNodeCounter / 150.0, 1.0);
        vec3 col = SpectralJet(a);
        irradiance = col;
    }

    vec3 lastFrameIrradiance = imageLoad(ImgResult, imgCoord).rgb;
    irradiance = mix(lastFrameIrradiance, irradiance, 1.0 / (float(rayIndicesSSBO.AccumulatedSamples) + 1.0));
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