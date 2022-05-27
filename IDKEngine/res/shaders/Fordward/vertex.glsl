#version 460 core

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec3 Normal;

struct Mesh
{
    int InstanceCount;
    int MatrixStart;
    int NodeStart;
    int BLASDepth;
    int MaterialIndex;
    float Emissive;
    float NormalMapStrength;
    float SpecularChance;
    float Roughness;
    float RefractionChance;
};

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 4) restrict readonly buffer MatrixSSBO
{
    mat4 Models[];
} matrixSSBO;

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
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    int Frame;
    float VelScale;
} taaDataUBO;

out InOutVars
{
    vec2 TexCoord;
    vec3 FragPos;
    vec4 ClipPos;
    vec4 PrevClipPos;
    vec3 Normal;
    mat3 TBN;
    flat int MeshIndex;
    flat int MaterialIndex;
    flat float Emissive;
    flat float NormalMapStrength;
    flat float SpecularBias;
    flat float RoughnessBias;
} outData;

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    mat4 model = matrixSSBO.Models[mesh.MatrixStart + gl_InstanceID];

    vec3 c1 = cross(Normal, vec3(0.0, 0.0, 1.0));
    vec3 c2 = cross(Normal, vec3(0.0, 1.0, 0.0));
    vec3 tangent = dot(c1, c1) > dot(c2, c2) ? c1 : c2;
    
    vec3 T = normalize((model * vec4(tangent, 0.0)).xyz);
    vec3 N = normalize((model * vec4(Normal, 0.0)).xyz);
    T = normalize(T - dot(T, N) * N);
    vec3 B = cross(N, T);

    outData.TBN = mat3(T, B, N);
    outData.TexCoord = TexCoord;
    outData.FragPos = (model * vec4(Position, 1.0)).xyz;
    outData.ClipPos = basicDataUBO.ProjView * vec4(outData.FragPos, 1.0);
    
    outData.PrevClipPos = basicDataUBO.PrevProjView * vec4(outData.FragPos, 1.0);
    
    outData.Normal = Normal;
    outData.MeshIndex = gl_DrawID;
    outData.MaterialIndex = mesh.MaterialIndex;
    outData.Emissive = mesh.Emissive;
    outData.NormalMapStrength = mesh.NormalMapStrength;
    outData.SpecularBias = mesh.SpecularChance;
    outData.RoughnessBias = mesh.Roughness;
    
    int rawIndex = taaDataUBO.Frame % taaDataUBO.Samples;
    vec2 offset = vec2(
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) * 2 + 0],
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) * 2 + 1]
    );

    vec4 jitteredClipPos = outData.ClipPos;
    jitteredClipPos.xy += offset * outData.ClipPos.w * taaDataUBO.Enabled;
    
    gl_Position = jitteredClipPos;
}