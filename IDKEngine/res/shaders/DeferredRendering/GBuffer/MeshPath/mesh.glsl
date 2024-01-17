#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_KHR_shader_subgroup_ballot : require

#pragma optionNV(unroll all)

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)

layout(local_size_x = 32) in;
layout(triangles, max_primitives = MESHLET_MAX_TRIANGLE_COUNT, max_vertices = MESHLET_MAX_VERTEX_COUNT) out;

struct DrawElementsCmd
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;

    uint BlasRootNodeIndex;
};

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
};

struct Vertex
{
    vec2 TexCoord;
    uint Tangent;
    uint Normal;
};

struct Meshlet
{
    uint VertexOffset;
    uint IndicesOffset;

    uint8_t VertexCount;
    uint8_t TriangleCount;
};

layout(std430, binding = 0) restrict readonly buffer DrawElementsCmdSSBO
{
    DrawElementsCmd DrawCommands[];
} drawElementsCmdSSBO;

layout(std430, binding = 2, row_major) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 4) restrict readonly buffer VertexSSBO
{
    Vertex Vertices[];
} vertexSSBO;

struct PackedVec3 { float x, y, z; };
layout(std430, binding = 10) restrict readonly buffer VertexPositionsSSBO
{
    PackedVec3 VertexPositions[];
} vertexPositionsSSBO;

layout(std430, binding = 12) restrict readonly buffer MeshletSSBO
{
    Meshlet Meshlets[];
} meshletSSBO;

layout(std430, binding = 14) restrict readonly buffer MeshletVertexIndicesSSBO
{
    uint VertexIndices[];
} meshletVertexIndicesSSBO;

layout(std430, binding = 15) restrict readonly buffer MeshletLocalIndicesSSBO
{
    uint8_t Indices[];
} meshletLocalIndicesSSBO;

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
    float DeltaRenderTime;
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
    vec4 PrevClipPos;
    vec3 Normal;
    vec3 Tangent;
    perprimitiveNV uint MeshID;
} outData[];

taskNV in InOutVars
{
    uint MeshID;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[32];
} inData;

void main()
{
    uint meshID = inData.MeshID;
    uint meshletID = inData.MeshletsStart + inData.SurvivingMeshlets[gl_WorkGroupID.x];

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance];
    Meshlet meshlet = meshletSSBO.Meshlets[meshletID];

    uint verticesPerInvocationRounded = (MESHLET_MAX_VERTEX_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < verticesPerInvocationRounded; i++)
    {
        uint8_t meshletVertexID = uint8_t(min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, meshlet.VertexCount - 1u));
        uint meshVertexID = meshlet.VertexOffset + meshletVertexID;
        uint globalVertexID = drawCmd.BaseVertex + meshletVertexIndicesSSBO.VertexIndices[meshVertexID];

        Vertex meshVertex = vertexSSBO.Vertices[globalVertexID];
        PackedVec3 vertexPosition = vertexPositionsSSBO.VertexPositions[globalVertexID];

        outData[meshletVertexID].TexCoord = meshVertex.TexCoord;

        vec3 position = vec3(vertexPosition.x, vertexPosition.y, vertexPosition.z);
        vec3 normal = DecompressSR11G11B10(meshVertex.Normal);
        vec3 tangent = DecompressSR11G11B10(meshVertex.Tangent);
        mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
        mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
        mat4 prevModelMatrix = mat4(meshInstance.PrevModelMatrix);

        mat3 unitVecToWorld = mat3(transpose(invModelMatrix));
        outData[meshletVertexID].Normal = normalize(unitVecToWorld * normal);
        outData[meshletVertexID].Tangent = normalize(unitVecToWorld * tangent);
        outData[meshletVertexID].PrevClipPos = basicDataUBO.PrevProjView * prevModelMatrix * vec4(position, 1.0);

        vec4 clipPos = basicDataUBO.ProjView * modelMatrix * vec4(position, 1.0);

        // Add jitter independent of perspective by multiplying with w
        vec4 jitteredClipPos = clipPos;
        jitteredClipPos.xy += taaDataUBO.Jitter * jitteredClipPos.w;

        // if (meshID == 67)
        // {
        //     for (int i = 0; i < 1000000; i++)
        //     {
        //         jitteredClipPos.x += i / 100000000000000.0;
        //     }
        // }

        gl_MeshVerticesNV[meshletVertexID].gl_Position = jitteredClipPos;
    }

    uint trianglesPerInvocationRounded = (MESHLET_MAX_TRIANGLE_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < trianglesPerInvocationRounded; i++)
    {
        uint8_t meshletTriangleID = uint8_t(min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, meshlet.TriangleCount - 1u));
        u8vec3 indices = u8vec3(
            meshletLocalIndicesSSBO.Indices[meshlet.IndicesOffset + meshletTriangleID * 3u + 0u],
            meshletLocalIndicesSSBO.Indices[meshlet.IndicesOffset + meshletTriangleID * 3u + 1u],
            meshletLocalIndicesSSBO.Indices[meshlet.IndicesOffset + meshletTriangleID * 3u + 2u]
        );

        // TODO: Try writePackedPrimitiveIndices4x8NV(i * 4, indices4);
        gl_PrimitiveIndicesNV[meshletTriangleID * 3u + 0u] = indices.x;
        gl_PrimitiveIndicesNV[meshletTriangleID * 3u + 1u] = indices.y;
        gl_PrimitiveIndicesNV[meshletTriangleID * 3u + 2u] = indices.z;

        outData[meshletTriangleID].MeshID = meshID;
    }

    if (gl_LocalInvocationIndex == 0)
    {
        gl_PrimitiveCountNV = meshlet.TriangleCount;
    }
}