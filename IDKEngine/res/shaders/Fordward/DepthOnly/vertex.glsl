#version 460 core

layout(location = 0) in vec3 Position;

struct Mesh
{
    mat4 Model;
    mat4 PrevModel;
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
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

void main()
{
    mat4 model = meshSSBO.Meshes[gl_DrawID].Model;
    vec3 fragPos = (model * vec4(Position, 1.0)).xyz;

    gl_Position = basicDataUBO.ProjView * vec4(fragPos, 1.0);
}