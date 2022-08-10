#version 460 core

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba32f) restrict uniform image2D ImgResult;

struct TransportRay
{
    vec3 Origin;
    uint IsRefractive;

    vec3 Direction;
    float CurrentIOR;

    vec3 Throughput;
    uint DebugFirstHitInteriorNodeCounter;

    vec3 Radiance;
    float _pad1;
};

layout(std430, binding = 6) restrict writeonly buffer TransportRaySSBO
{
    TransportRay Rays[];
} transportRaySSBO;

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
    float Time;
} basicDataUBO;

vec3 SpectralJet(float w);

uniform bool IsDebugBVHTraversal;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    const uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;
    TransportRay transportRay = transportRaySSBO.Rays[rayIndex];

    vec3 irradiance = transportRay.Radiance;
    if (IsDebugBVHTraversal)
    {
        // use visible light spectrum as heatmap
        float waveLength = min(transportRay.DebugFirstHitInteriorNodeCounter * 2.5 + 400.0, 700.0);
        vec3 col = SpectralJet(waveLength);
        irradiance = col;
    }

    vec3 lastFrameColor = imageLoad(ImgResult, imgCoord).rgb;
    irradiance = mix(lastFrameColor, irradiance, 1.0 / (basicDataUBO.FreezeFrameCounter + 1.0));
    imageStore(ImgResult, imgCoord, vec4(irradiance, 1.0));
}

// Source: https://www.shadertoy.com/view/ls2Bz1
vec3 SpectralJet(float w)
{
	float x = clamp((w - 400.0) / 300.0, 0.0, 1.0);
	vec3 c;

	if (x < 0.25)
		c = vec3(0.0, 4.0 * x, 1.0);
	else if (x < 0.5)
		c = vec3(0.0, 1.0, 1.0 + 4.0 * (0.25 - x));
	else if (x < 0.75)
		c = vec3(4.0 * (x - 0.5), 1.0, 0.0);
	else
		c = vec3(1.0, 1.0 + 4.0 * (0.75 - x), 0.0);

	return clamp(c, vec3(0.0), vec3(1.0));
}