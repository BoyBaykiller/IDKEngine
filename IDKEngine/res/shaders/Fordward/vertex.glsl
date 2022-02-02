#version 460 core

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in vec3 Normal;
layout(location = 3) in vec3 Tangent;
layout(location = 4) in vec3 BiTangent;

struct Mesh
{
    mat4 Model[1];
    int MaterialIndex;
    int BVHEntry;
    int _pad0;
    int _pad1;
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
    mat4 Projection;
    mat4 InvProjection;
} basicDataUBO;

out InOutVars
{
    vec2 TexCoord;
    vec3 FragPos;
    mat3 TBN;
    flat int MeshIndex;
    flat int MaterialIndex;
} outData;

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];

    vec3 T = normalize(vec3(mesh.Model[0] * vec4(Tangent, 0.0)));
    vec3 N = normalize(vec3(mesh.Model[0] * vec4(Normal, 0.0)));
    T = normalize(T - dot(T, N) * N);
    vec3 B = BiTangent;

    outData.TBN = mat3(T, B, N);
    outData.TexCoord = TexCoord;
    outData.FragPos = (mesh.Model[0] * vec4(Position, 1.0)).xyz;

    outData.MeshIndex = gl_DrawID;
    outData.MaterialIndex = mesh.MaterialIndex;

    gl_Position = basicDataUBO.ProjView * vec4(outData.FragPos, 1.0);
}