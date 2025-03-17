#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_KHR_shader_subgroup_ballot : require

#define DECLARE_MESHLET_STORAGE_BUFFERS
#define DECLARE_MESHLET_RENDERING_TYPES

AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

#define MESHLETS_PER_WORKGROUP 32
layout(local_size_x = MESHLETS_PER_WORKGROUP) in;

taskNV out InOutData
{
    uint MeshId;
    uint InstanceId;
    uint8_t FaceId;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[MESHLETS_PER_WORKGROUP];
} outData;

layout(location = 0) uniform int ShadowIndex;

void main()
{
    uint faceAndMeshInstanceId = visibleMeshInstanceIdSSBO.Ids[gl_DrawID];
    uint8_t faceId = uint8_t(faceAndMeshInstanceId >> 29);
    uint meshInstanceId = faceAndMeshInstanceId & ((1u << 29) - 1);

    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceId];

    uint meshId = meshInstance.MeshId;
    GpuMesh mesh = meshSSBO.Meshes[meshId];

    if (gl_GlobalInvocationID.x >= mesh.MeshletCount)
    {
        return;
    }

    uint8_t localMeshlet = uint8_t(gl_LocalInvocationIndex);
    uint workgroupFirstMeshlet = mesh.MeshletsStart + (gl_WorkGroupID.x * MESHLETS_PER_WORKGROUP);
    uint workgroupThisMeshlet = workgroupFirstMeshlet + localMeshlet;

    GpuDrawElementsCmd drawCmd = drawElementsCmdSSBO.Commands[meshId];
    GpuMeshletInfo meshletInfo = meshletInfoSSBO.MeshletsInfo[workgroupThisMeshlet];
    
    mat4 projView = shadowsUBO.PointShadows[ShadowIndex].ProjViewMatrices[faceId];
    mat4 modelMatrix = mat4(meshInstanceSSBO.MeshInstances[meshInstanceId].ModelMatrix);

    Frustum frustum = GetFrustum(projView * modelMatrix);
    bool isVisible = FrustumBoxIntersect(frustum, Box(meshletInfo.Min, meshletInfo.Max));
    
    uvec4 visibleMeshletsBitmask = subgroupBallot(isVisible);
    if (isVisible)
    {
        uint offset = subgroupBallotExclusiveBitCount(visibleMeshletsBitmask);
        outData.SurvivingMeshlets[offset] = localMeshlet;
    }

    if (gl_LocalInvocationIndex == 0)
    {
        outData.MeshId = meshId;
        outData.InstanceId = meshInstanceId;
        outData.FaceId = faceId;
        outData.MeshletsStart = workgroupFirstMeshlet;
        
        uint survivingCount = subgroupBallotBitCount(visibleMeshletsBitmask);
        gl_TaskCountNV = survivingCount;
    }
}