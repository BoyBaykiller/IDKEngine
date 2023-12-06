#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in uint Tangent;
layout(location = 3) in uint Normal;

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

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    float MipmapBias;
    int TemporalAntiAliasingMode;
} taaDataUBO;

out InOutVars
{
    vec2 TexCoord;
    vec4 ClipPos;
    vec4 PrevClipPos;
    vec3 Normal;
    mat3 TBN;
    uint MaterialIndex;
    float EmissiveBias;
    float NormalMapStrength;
    float SpecularBias;
    float RoughnessBias;
} outData;

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance];
    
    vec3 normal = DecompressSR11G11B10(Normal);
    vec3 tangent = DecompressSR11G11B10(Tangent);

    outData.TBN = GetTBN(mat3(meshInstance.ModelMatrix), tangent, normal);
    outData.TexCoord = TexCoord;
    
    vec3 worldPos = (meshInstance.ModelMatrix * vec4(Position, 1.0)).xyz;
    outData.ClipPos = basicDataUBO.ProjView * vec4(worldPos, 1.0);
    outData.PrevClipPos = basicDataUBO.PrevProjView * meshInstance.PrevModelMatrix * vec4(Position, 1.0);
    
    outData.Normal = mat3(meshInstance.ModelMatrix) * normal;
    outData.MaterialIndex = mesh.MaterialIndex;
    outData.EmissiveBias = mesh.EmissiveBias;
    outData.NormalMapStrength = mesh.NormalMapStrength;
    outData.SpecularBias = mesh.SpecularBias;
    outData.RoughnessBias = mesh.RoughnessBias;

    // Add jitter independent of perspective by multypling with w
    vec4 jitteredClipPos = outData.ClipPos;
    jitteredClipPos.xy += taaDataUBO.Jitter * outData.ClipPos.w;
    
    gl_Position = jitteredClipPos;
}
