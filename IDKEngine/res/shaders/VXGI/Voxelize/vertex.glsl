#version 460 core

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 3) in uint Normal;

struct Mesh
{
    int InstanceCount;
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    vec3 Absorbance;
    uint CubemapShadowCullInfo;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 4) restrict readonly buffer MatrixSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    float _pad0;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

out InOutVars
{
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} outData;

vec3 DecompressSNorm32Fast(uint data);

void main()
{
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance];

    vec3 normal = DecompressSNorm32Fast(Normal);

    mat3 localToWorld = mat3(transpose(meshInstance.InvModelMatrix));
    outData.Normal = normalize(localToWorld * normal);
    outData.TexCoord = TexCoord;

    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    outData.MaterialIndex = mesh.MaterialIndex;
    outData.EmissiveBias = mesh.EmissiveBias;

    gl_Position = meshInstance.ModelMatrix * vec4(Position, 1.0);
}

vec3 DecompressSNorm32Fast(uint data)
{
    float r = (data >> 0) & ((1u << 11) - 1);
    float g = (data >> 11) & ((1u << 11) - 1);
    float b = (data >> 22) & ((1u << 10) - 1);

    r /= (1u << 11) - 1;
    g /= (1u << 11) - 1;
    b /= (1u << 10) - 1;

    return vec3(r, g, b) * 2.0 - 1.0;
}