#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_KHR_shader_subgroup_ballot : require

#pragma optionNV(unroll all)

#define MESHLET_VERTEX_COUNT 128
#define MESHLET_PRIMITIVE_COUNT 252

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)

layout(local_size_x = 32) in;
layout(triangles, max_primitives = MESHLET_PRIMITIVE_COUNT, max_vertices = MESHLET_VERTEX_COUNT) out;

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
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
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

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 4) restrict readonly buffer VertexSSBO
{
    Vertex Vertices[];
} vertexSSBO;

struct PackedVec3 { float x, y, z; };
layout(std430, binding = 10) restrict readonly buffer VertexPositions
{
    PackedVec3 VertexPositions[];
} vertexPositionsSSBO;

layout(std430, binding = 11) restrict readonly buffer MeshletSSBO
{
    Meshlet Meshlets[];
} meshletSSBO;

layout(std430, binding = 13) restrict readonly buffer MeshletVertexIndices
{
    uint VertexIndices[];
} meshletVertexIndicesSSBO;

layout(std430, binding = 14) restrict readonly buffer MeshletPrimitiveIndices
{
    uint8_t Indices[];
} meshletPrimitiveIndicesSSBO;

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
    vec4 ClipPos;
    vec4 PrevClipPos;
    vec3 Normal;
    mat3 TBN;
    perprimitiveNV uint MeshID;
} outData[];

taskNV in InOutVars
{
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[32];
} inData;

void main()
{
    uint meshID = gl_DrawID;
    uint meshletID = inData.MeshletsStart + inData.SurvivingMeshlets[gl_WorkGroupID.x]; 

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance];
    Meshlet meshlet = meshletSSBO.Meshlets[meshletID];

    uint verticesPerInvocationRounded = (MESHLET_VERTEX_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < verticesPerInvocationRounded; i++)
    {
        uint meshletVertexID = min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, uint(meshlet.VertexCount) - 1);
        uint meshVertexID = meshlet.VertexOffset + meshletVertexID;
        uint globalVertexID = drawCmd.BaseVertex + meshletVertexIndicesSSBO.VertexIndices[meshVertexID];

        Vertex meshVertex = vertexSSBO.Vertices[globalVertexID];
        PackedVec3 vertexPosition = vertexPositionsSSBO.VertexPositions[globalVertexID];

        vec3 normal = DecompressSR11G11B10(meshVertex.Normal);
        vec3 tangent = DecompressSR11G11B10(meshVertex.Tangent);
        vec3 position = vec3(vertexPosition.x, vertexPosition.y, vertexPosition.z);

        mat3 normalToWorld = mat3(transpose(meshInstance.InvModelMatrix));
        outData[meshletVertexID].Normal = normalToWorld * normal;

        outData[meshletVertexID].ClipPos = basicDataUBO.ProjView * meshInstance.ModelMatrix * vec4(position, 1.0);
        outData[meshletVertexID].PrevClipPos = basicDataUBO.PrevProjView * meshInstance.PrevModelMatrix * vec4(position, 1.0);
        
        outData[meshletVertexID].TexCoord = meshVertex.TexCoord;
        outData[meshletVertexID].TBN = GetTBN(mat3(meshInstance.ModelMatrix), tangent, normal);

        // Add jitter independent of perspective by multiplying with w
        vec4 jitteredClipPos = outData[meshletVertexID].ClipPos;
        jitteredClipPos.xy += taaDataUBO.Jitter * jitteredClipPos.w;

        gl_MeshVerticesNV[meshletVertexID].gl_Position = jitteredClipPos;
    }

    uint primitiveCount = 0;
    uint primitivesPerInvocationRounded = (MESHLET_PRIMITIVE_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < primitivesPerInvocationRounded; i++)
    {
        uint meshletTriangleID = min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, uint(meshlet.TriangleCount) - 1);
        uvec3 indices = uvec3(meshletPrimitiveIndicesSSBO.Indices[meshlet.IndicesOffset + meshletTriangleID * 3 + 0],
                            meshletPrimitiveIndicesSSBO.Indices[meshlet.IndicesOffset + meshletTriangleID * 3 + 1],
                            meshletPrimitiveIndicesSSBO.Indices[meshlet.IndicesOffset + meshletTriangleID * 3 + 2]);

        vec4 clipPos0 = gl_MeshVerticesNV[indices.x].gl_Position;
        vec4 clipPos1 = gl_MeshVerticesNV[indices.y].gl_Position;
        vec4 clipPos2 = gl_MeshVerticesNV[indices.z].gl_Position;
        bool visible = true;
        // visible = determinant(mat3(clipPos0.xyz, clipPos1.xyz, clipPos2.xyz)) > 0.0;  // not worth it

        uvec4 visibleTriBitmask = subgroupBallot(visible);
        uint visibleCount = subgroupBallotBitCount(visibleTriBitmask);

        uint offset = primitiveCount + subgroupBallotExclusiveBitCount(visibleTriBitmask);

        if (visible)
        {
            // TODO: Try writePackedPrimitiveIndices4x8NV(i * 4, indices4);
            gl_PrimitiveIndicesNV[offset * 3 + 0] = indices.x;
            gl_PrimitiveIndicesNV[offset * 3 + 1] = indices.y;
            gl_PrimitiveIndicesNV[offset * 3 + 2] = indices.z;

            outData[offset].MeshID = meshID;

        }
        primitiveCount += visibleCount; 
    }

    if (gl_LocalInvocationIndex == 0)
    {
        gl_PrimitiveCountNV = primitiveCount;
    }
}