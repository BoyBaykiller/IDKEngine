#version 460 core

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec3 Normal;
layout(location = 3) in vec3 Tangent;
layout(location = 4) in vec3 BiTangent;

struct Mesh
{
    mat4 Model;
    int MaterialIndex;
    int BVHEntry;
    float Emissive;
    float NormalMapStrength;
    float SpecularChance;
    float Roughness;
    float _pad0;
    float _pad1;
};

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

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

layout(std140, binding = 5) uniform TaaDataUBO
{
    vec4 Jitters[18 / 2];
    int Samples;
    int Enabled;
    int Frame;
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
} outData;

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];

    vec3 T = normalize(vec3(mesh.Model * vec4(Tangent, 0.0)));
    vec3 N = normalize(vec3(mesh.Model * vec4(Normal, 0.0)));
    T = normalize(T - dot(T, N) * N);
    vec3 B = BiTangent;

    outData.TBN = mat3(T, B, N);
    outData.TexCoord = TexCoord;
    outData.FragPos = (mesh.Model * vec4(Position, 1.0)).xyz;
    outData.ClipPos = basicDataUBO.ProjView * vec4(outData.FragPos, 1.0);
    
    outData.PrevClipPos = basicDataUBO.PrevProjView * vec4(outData.FragPos, 1.0);
    
    outData.Normal = Normal;
    outData.MeshIndex = gl_DrawID;
    outData.MaterialIndex = mesh.MaterialIndex;
    outData.Emissive = mesh.Emissive;
    outData.NormalMapStrength = mesh.NormalMapStrength;
    
    int rawIndex = taaDataUBO.Frame % taaDataUBO.Samples;
    vec2 offset = vec2(
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) + 0],
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) + 1]
    );

    vec4 jitteredClipPos = outData.ClipPos;
    jitteredClipPos.xy += offset * outData.ClipPos.w * taaDataUBO.Enabled;
    
    gl_Position = jitteredClipPos;
}