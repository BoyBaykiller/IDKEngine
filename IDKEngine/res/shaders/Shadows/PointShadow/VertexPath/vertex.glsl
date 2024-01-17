#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)

layout(location = 0) in vec3 Position;

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
};

struct PointShadow
{
    samplerCube Texture;
    samplerCubeShadow ShadowTexture;

    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    vec3 _pad0;
    float FarPlane;
};

layout(std430, binding = 2, row_major) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int FaceIndex;

void main()
{
    mat4 modelMatrix = mat4(meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance].ModelMatrix);
    vec3 fragPos = vec3(modelMatrix * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[FaceIndex] * vec4(fragPos, 1.0);
}