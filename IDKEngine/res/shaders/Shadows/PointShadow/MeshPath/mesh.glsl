#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_ARB_bindless_texture : require
#extension GL_KHR_shader_subgroup_ballot : require

#pragma optionNV(unroll all)

AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)

layout(local_size_x = 32) in;
// We write out indices in packs of 4 using writePackedPrimitiveIndices4x8NV as an optimization.
// Because triangle indices count might not be divisble by 4, we need to overshoot written indices to not miss any.
// To prevent out of bounds access we pad by 1
layout(triangles, max_primitives = MESHLET_MAX_TRIANGLE_COUNT + 1, max_vertices = MESHLET_MAX_VERTEX_COUNT) out;

struct DrawElementsCmd
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
    vec3 _pad0;
    uint MeshIndex;
};

struct Meshlet
{
    uint VertexOffset;
    uint IndicesOffset;

    uint8_t VertexCount;
    uint8_t TriangleCount;
};

struct PointShadow
{
    samplerCube Texture;
    samplerCubeShadow ShadowTexture;

    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    vec3 _pad0;
    float FarPlane;
};

layout(std430, binding = 0) restrict readonly buffer DrawElementsCmdSSBO
{
    DrawElementsCmd DrawCommands[];
} drawElementsCmdSSBO;

layout(std430, binding = 2, row_major) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

struct PackedVec3 { float x, y, z; };
layout(std430, binding = 12) restrict readonly buffer VertexPositionsSSBO
{
    PackedVec3 VertexPositions[];
} vertexPositionsSSBO;

layout(std430, binding = 14) restrict readonly buffer MeshletSSBO
{
    Meshlet Meshlets[];
} meshletSSBO;

layout(std430, binding = 16) restrict readonly buffer MeshletVertexIndicesSSBO
{
    uint VertexIndices[];
} meshletVertexIndicesSSBO;

layout(std430, binding = 17) restrict readonly buffer MeshletLocalIndicesSSBO
{
    uint PackedIndices[];
} meshletLocalIndicesSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

taskNV in InOutVars
{
    uint MeshID;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[32];
} inData;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int FaceIndex;

void main()
{
    uint meshID = inData.MeshID;
    uint meshletID = inData.MeshletsStart + inData.SurvivingMeshlets[gl_WorkGroupID.x]; 

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance];
    Meshlet meshlet = meshletSSBO.Meshlets[meshletID];

    const uint verticesPerInvocationRounded = (MESHLET_MAX_VERTEX_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < verticesPerInvocationRounded; i++)
    {
        uint8_t meshletVertexID = uint8_t(min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, meshlet.VertexCount - 1u));
        uint meshVertexID = meshlet.VertexOffset + meshletVertexID;
        uint globalVertexID = drawCmd.BaseVertex + meshletVertexIndicesSSBO.VertexIndices[meshVertexID];

        PackedVec3 vertexPosition = vertexPositionsSSBO.VertexPositions[globalVertexID];
        vec3 position = vec3(vertexPosition.x, vertexPosition.y, vertexPosition.z);

        mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
        vec3 fragPos = vec3(modelMatrix * vec4(position, 1.0));
        vec4 clipPos = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[FaceIndex] * vec4(fragPos, 1.0);

        gl_MeshVerticesNV[meshletVertexID].gl_Position = clipPos;
    }

    const uint meshletMaxPackedIndices = MESHLET_MAX_TRIANGLE_COUNT * 3 / 4;
    const uint packedIndicesPerInvocationRounded = (meshletMaxPackedIndices + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (uint i = 0; i < packedIndicesPerInvocationRounded; i++)
    {
        uint packedIndicesID = gl_LocalInvocationIndex + i * gl_WorkGroupSize.x;
        uint indicesID = min(packedIndicesID * 4, meshlet.TriangleCount * 3u);

        uint indices4 = meshletLocalIndicesSSBO.PackedIndices[meshlet.IndicesOffset / 4 + packedIndicesID];
        writePackedPrimitiveIndices4x8NV(indicesID, indices4);
    }

    if (gl_LocalInvocationIndex == 0)
    {
        gl_PrimitiveCountNV = meshlet.TriangleCount;
    }
}