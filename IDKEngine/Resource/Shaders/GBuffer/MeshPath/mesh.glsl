#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_KHR_shader_subgroup_ballot : require

#pragma optionNV(unroll all)

#define DECLARE_MESHLET_STORAGE_BUFFERS
#define DECLARE_MESHLET_RENDERING_TYPES

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

layout(local_size_x = 32) in;
// We write out indices in packs of 4 using writePackedPrimitiveIndices4x8NV as an optimization.
// Because triangle indices count might not be divisble by 4, we need to overshoot written indices to not miss any.
// To prevent out of bounds access we pad by 1
layout(triangles, max_primitives = MESHLET_MAX_TRIANGLE_COUNT + 1, max_vertices = MESHLET_MAX_VERTEX_COUNT) out;

out InOutData
{
    vec2 TexCoord;
    vec4 PrevClipPos;
    vec3 Normal;
    vec3 Tangent;
    perprimitiveNV uint MeshId;
} outData[MESHLET_MAX_VERTEX_COUNT];

taskNV in InOutData
{
    uint MeshId;
    uint InstanceID;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[32];
} inData;

void main()
{
    uint meshID = inData.MeshId;
    uint instanceID = inData.InstanceID;
    uint meshletID = inData.MeshletsStart + inData.SurvivingMeshlets[gl_WorkGroupID.x];

    GpuDrawElementsCmd drawCmd = drawElementsCmdSSBO.Commands[meshID];
    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[instanceID];
    GpuMeshlet meshlet = meshletSSBO.Meshlets[meshletID];

    const uint verticesPerInvocationRounded = (MESHLET_MAX_VERTEX_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < verticesPerInvocationRounded; i++)
    {
        uint8_t meshletVertexID = uint8_t(min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, meshlet.VertexCount - 1u));
        uint meshVertexID = meshlet.VertexOffset + meshletVertexID;
        uint globalVertexID = drawCmd.BaseVertex + meshletVertexIndicesSSBO.VertexIndices[meshVertexID];

        GpuVertex meshVertex = vertexSSBO.Vertices[globalVertexID];
        PackedVec3 vertexPosition = vertexPositionsSSBO.Positions[globalVertexID];
        vec3 prevVertexPosition = Unpack(prevVertexPositionSSBO.Positions[globalVertexID]);

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
        outData[meshletVertexID].PrevClipPos = perFrameDataUBO.PrevProjView * prevModelMatrix * vec4(prevVertexPosition, 1.0);

        vec4 clipPos = perFrameDataUBO.ProjView * modelMatrix * vec4(position, 1.0);

        // Add jitter independent of perspective by multiplying with w
        vec4 jitteredClipPos = clipPos;
        jitteredClipPos.xy += taaDataUBO.Jitter * jitteredClipPos.w;

        gl_MeshVerticesNV[meshletVertexID].gl_Position = jitteredClipPos;
    }

    const uint meshletMaxPackedIndices = MESHLET_MAX_TRIANGLE_COUNT * 3 / 4;
    const uint packedIndicesPerInvocationRounded = (meshletMaxPackedIndices + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < packedIndicesPerInvocationRounded; i++)
    {
        uint packedIndicesID = gl_LocalInvocationIndex + i * gl_WorkGroupSize.x;
        uint indicesID = min(packedIndicesID * 4, meshlet.TriangleCount * 3u);

        uint indices4 = meshletLocalIndicesSSBO.PackedIndices[meshlet.IndicesOffset / 4 + packedIndicesID];
        writePackedPrimitiveIndices4x8NV(indicesID, indices4);
    }

    const uint trianglesPerInvocationRounded = (MESHLET_MAX_TRIANGLE_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < trianglesPerInvocationRounded; i++)
    {
        uint8_t meshletTriangleID = uint8_t(min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, meshlet.TriangleCount - 1u));

        outData[meshletTriangleID].MeshId = meshID;
    }

    if (gl_LocalInvocationIndex == 0)
    {
        gl_PrimitiveCountNV = meshlet.TriangleCount;
    }
}