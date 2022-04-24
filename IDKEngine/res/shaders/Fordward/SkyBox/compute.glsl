#version 460 core
#define DEPTH_CLEAR_COLOR 1.0
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerDepth;
layout(binding = 1) uniform samplerCube SamplerEnvironment;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    if (texture(SamplerDepth, uv).r != DEPTH_CLEAR_COLOR)
        return;

    vec3 dir = GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, uv * 2.0 - 1.0);
    imageStore(ImgResult, imgCoord, vec4(texture(SamplerEnvironment, dir).rgb, 1.0));
}

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords)
{
    vec4 rayEye = inverseProj * vec4(normalizedDeviceCoords, -1.0, 0.0);
    rayEye.zw = vec2(-1.0, 0.0);
    return normalize((inverseView * rayEye).xyz);
}