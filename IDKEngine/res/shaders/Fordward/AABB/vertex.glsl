#version 460 core

const vec3 positions[24] = vec3[]
(
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
);

struct Mesh
{
    mat4 Model;
    mat4 PrevModel;
    int MaterialIndex;
    int BaseNode;
    int _pad0;
    int _pad1;
};

struct Node
{
    vec3 Min;
    int VerticesStart;
    vec3 Max;
    int VerticesEnd;
};

layout(std430, binding = 1) restrict readonly buffer BVHSSBO
{
    Node Nodes[];
} bvhSSBO;

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
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_InstanceID];
    Node node = bvhSSBO.Nodes[mesh.BaseNode + 0];

    vec3 aabbPos = (node.Min + node.Max) * 0.5;
    vec3 aabbSize = node.Max - node.Min;

    gl_Position = basicDataUBO.ProjView * mesh.Model * vec4(aabbPos + aabbSize * positions[gl_VertexID], 1.0); 
}