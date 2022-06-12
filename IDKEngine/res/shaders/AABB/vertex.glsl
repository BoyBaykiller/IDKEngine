#version 460 core

const vec3 positions[24] =
{
    // Back
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5, -0.5, -0.5 ),
    vec3( 0.5, -0.5, -0.5 ),
    vec3( 0.5,  0.5, -0.5 ),

    // Front
    vec3(-0.5,  0.5,  0.5 ),
    vec3(-0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5,  0.5 ),
    vec3( 0.5,  0.5,  0.5 ),

    // Left
    vec3(-0.5,  0.5,  0.5 ),
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5, -0.5, -0.5 ),
    vec3(-0.5, -0.5,  0.5 ),

    // Right
    vec3( 0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5, -0.5 ),
    vec3( 0.5, -0.5, -0.5 ),
    vec3( 0.5, -0.5,  0.5 ),

    // Up
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5, -0.5 ),

    // Down
    vec3(-0.5, -0.5, -0.5 ),
    vec3(-0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5, -0.5 )
};

struct DrawCommand
{
    int Count;
    int InstanceCount;
    int FirstIndex;
    int BaseVertex;
    int BaseInstance;
};

struct Mesh
{
    int InstanceCount;
    int VisibleCubemapFacesInfo;
    int MaterialIndex;
    float Emissive;
    float NormalMapStrength;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
};

struct Node
{
    vec3 Min;
    uint TriStartOrLeftChild;
    vec3 Max;
    uint TriCount;
};

layout(std430, binding = 0) restrict readonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandsSSBO;

layout(std430, binding = 1) restrict readonly buffer BVHSSBO
{
    Node Nodes[];
} bvhSSBO;

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


layout(location = 0) uniform int MeshIndex;

void main()
{
    DrawCommand meshCMD = drawCommandsSSBO.DrawCommands[MeshIndex];
    Node node = bvhSSBO.Nodes[2 * (meshCMD.FirstIndex / 3)];
    mat4 model = matrixSSBO.Models[meshCMD.BaseInstance + gl_InstanceID];

    vec3 aabbPos = (node.Min + node.Max) * 0.5;
    vec3 aabbSize = node.Max - node.Min;

    gl_Position = basicDataUBO.ProjView * model * vec4(aabbPos + aabbSize * positions[gl_VertexID], 1.0);
}