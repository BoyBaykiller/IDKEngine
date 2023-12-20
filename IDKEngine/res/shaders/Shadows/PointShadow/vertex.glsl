#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)

layout(location = 0) in vec3 Position;

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

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    float _pad0;
    vec3 Absorbance;
    uint CubemapShadowCullInfo;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int Layer;

void main()
{
    mat4 model = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance].ModelMatrix;
    vec3 fragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[Layer] * vec4(fragPos, 1.0);

}