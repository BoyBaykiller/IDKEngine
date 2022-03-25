#version 460 core

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;

struct Mesh
{
    mat4 Model;
    mat4 PrevModel;
    int MaterialIndex;
    int BVHEntry;
    float Emissive;
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
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

out InOutVars
{
    vec2 TexCoord;
    flat int MaterialIndex;
} outData;

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    mat4 model = mesh.Model;
    vec3 fragPos = (model * vec4(Position, 1.0)).xyz;
    outData.MaterialIndex = mesh.MaterialIndex;
    outData.TexCoord = TexCoord;

    gl_Position = basicDataUBO.ProjView * vec4(fragPos, 1.0);
}