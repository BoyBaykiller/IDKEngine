#version 460 core
#extension GL_ARB_bindless_texture : require

layout(location = 0) in vec3 Position;
layout(location = 1) in float TexCoordU;
layout(location = 2) in vec3 Normal;
layout(location = 3) in float TexCoordV;
layout(location = 4) in vec3 Tangent;

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
    int VisibleCubemapFacesInfo;
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

layout(std140, binding = 3) uniform TaaDataUBO
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
    flat int MaterialIndex;
    flat float EmissiveBias;
    flat float NormalMapStrength;
    flat float SpecularBias;
    flat float RoughnessBias;
} outData;

void main()
{
    mat4 model = matrixSSBO.Models[gl_BaseInstance + gl_InstanceID];
    vec3 T = normalize((model * vec4(Tangent, 0.0)).xyz);
    vec3 N = normalize((model * vec4(Normal, 0.0)).xyz);
    T = normalize(T - dot(T, N) * N);
    vec3 B = cross(N, T);

    outData.TBN = mat3(T, B, N);
    outData.TexCoord = vec2(TexCoordU, TexCoordV);
    outData.FragPos = (model * vec4(Position, 1.0)).xyz;
    outData.ClipPos = basicDataUBO.ProjView * vec4(outData.FragPos, 1.0);
    
    outData.PrevClipPos = basicDataUBO.PrevProjView * vec4(outData.FragPos, 1.0);
    
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    outData.Normal = Normal;
    outData.MaterialIndex = mesh.MaterialIndex;
    outData.EmissiveBias = mesh.EmissiveBias;
    outData.NormalMapStrength = mesh.NormalMapStrength;
    outData.SpecularBias = mesh.SpecularBias;
    outData.RoughnessBias = mesh.RoughnessBias;
    
    int rawIndex = taaDataUBO.Frame % taaDataUBO.Samples;
    vec2 offset = vec2(
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) * 2 + 0],
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) * 2 + 1]
    );

    vec4 jitteredClipPos = outData.ClipPos;
    jitteredClipPos.xy += offset * outData.ClipPos.w * taaDataUBO.Enabled;
    
    gl_Position = jitteredClipPos;
}