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
    perprimitiveNV uint MaterialId;
} outData[MESHLET_MAX_VERTEX_COUNT];

taskNV in InOutData
{
    uint MeshId;
    uint InstanceId;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[32];
} inData;

void main()
{
    uint meshId = inData.MeshId;
    uint instanceId = inData.InstanceId;
    uint meshletId = inData.MeshletsStart + inData.SurvivingMeshlets[gl_WorkGroupID.x];

    GpuDrawElementsCmd drawCmd = drawElementsCmdSSBO.Commands[meshId];
    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[instanceId];
    GpuMeshlet meshlet = meshletSSBO.Meshlets[meshletId];

    uint materialId;
    const uint verticesPerInvocationRounded = (MESHLET_MAX_VERTEX_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < verticesPerInvocationRounded; i++)
    {
        uint8_t meshletVertexId = uint8_t(min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, meshlet.VertexCount - 1u));
        uint meshVertexId = meshlet.VertexOffset + meshletVertexId;
        uint globalVertexId = drawCmd.BaseVertex + meshletVertexIndicesSSBO.VertexIndices[meshVertexId];

        GpuVertex vertex = vertexSSBO.Vertices[globalVertexId];
        PackedVec3 vertexPosition = vertexPositionsSSBO.Positions[globalVertexId];
        vec3 prevVertexPosition = Unpack(prevVertexPositionSSBO.Positions[globalVertexId]);

        outData[meshletVertexId].TexCoord = Unpack(vertex.TexCoord);

        vec3 position = vec3(vertexPosition.x, vertexPosition.y, vertexPosition.z);
        vec3 normal = DecompressSR11G11B10(vertex.Normal);
        vec3 tangent = DecompressSR11G11B10(vertex.Tangent);
        mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
        mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
        mat4 prevModelMatrix = mat4(meshInstance.PrevModelMatrix);
        mat3 unitVecToWorld = mat3(transpose(invModelMatrix));

        outData[meshletVertexId].Normal = normalize(unitVecToWorld * normal);
        outData[meshletVertexId].Tangent = normalize(unitVecToWorld * tangent);
        outData[meshletVertexId].PrevClipPos = perFrameDataUBO.PrevProjView * prevModelMatrix * vec4(prevVertexPosition, 1.0);
        materialId = vertex.MaterialId;

        vec4 clipPos = perFrameDataUBO.ProjView * modelMatrix * vec4(position, 1.0);

        // Add jitter independent of perspective by multiplying with w
        vec4 jitteredClipPos = clipPos;
        jitteredClipPos.xy += taaDataUBO.Jitter * jitteredClipPos.w;

        gl_MeshVerticesNV[meshletVertexId].gl_Position = jitteredClipPos;
    }

    const uint meshletMaxPackedIndices = MESHLET_MAX_TRIANGLE_COUNT * 3 / 4;
    const uint packedIndicesPerInvocationRounded = (meshletMaxPackedIndices + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < packedIndicesPerInvocationRounded; i++)
    {
        uint packedIndicesId = gl_LocalInvocationIndex + i * gl_WorkGroupSize.x;
        uint indicesId = min(packedIndicesId * 4, meshlet.TriangleCount * 3u);

        uint indices4 = meshletLocalIndicesSSBO.PackedIndices[meshlet.IndicesOffset / 4 + packedIndicesId];
        writePackedPrimitiveIndices4x8NV(indicesId, indices4);
    }

    const uint trianglesPerInvocationRounded = (MESHLET_MAX_TRIANGLE_COUNT + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
    for (int i = 0; i < trianglesPerInvocationRounded; i++)
    {
        uint8_t meshletTriangleId = uint8_t(min(gl_LocalInvocationIndex + i * gl_WorkGroupSize.x, meshlet.TriangleCount - 1u));

        outData[meshletTriangleId].MeshId = meshId;
        outData[meshletTriangleId].MaterialId = materialId;
    }

    if (gl_LocalInvocationIndex == 0)
    {
        gl_PrimitiveCountNV = meshlet.TriangleCount;
    }
}