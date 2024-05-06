#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_KHR_shader_subgroup_ballot : require

#define DECLARE_MESHLET_STORAGE_BUFFERS
AppInclude(include/StaticStorageBuffers.glsl)

AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)

#define MESHLETS_PER_WORKGROUP 32
layout(local_size_x = MESHLETS_PER_WORKGROUP) in;

taskNV out InOutVars
{
    uint MeshID;
    uint InstanceID;
    uint8_t FaceID;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[MESHLETS_PER_WORKGROUP];
} outData;

layout(location = 0) uniform int ShadowIndex;

void main()
{
    uint faceAndMeshInstanceID = visibleMeshInstanceSSBO.MeshInstanceIDs[gl_DrawID];
    uint8_t faceID = uint8_t(faceAndMeshInstanceID >> 29);
    uint meshInstanceID = faceAndMeshInstanceID & ((1u << 29) - 1);

    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceID];

    uint meshID = meshInstance.MeshIndex;
    Mesh mesh = meshSSBO.Meshes[meshID];

    if (gl_GlobalInvocationID.x >= mesh.MeshletCount)
    {
        return;
    }

    uint8_t localMeshlet = uint8_t(gl_LocalInvocationIndex);
    uint workgroupFirstMeshlet = mesh.MeshletsStart + (gl_WorkGroupID.x * MESHLETS_PER_WORKGROUP);
    uint workgroupThisMeshlet = workgroupFirstMeshlet + localMeshlet;

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshID];
    MeshletInfo meshletInfo = meshletInfoSSBO.MeshletsInfo[workgroupThisMeshlet];
    
    mat4 projView = shadowsUBO.PointShadows[ShadowIndex].ProjViewMatrices[faceID];
    mat4 modelMatrix = mat4(meshInstanceSSBO.MeshInstances[meshInstanceID].ModelMatrix);

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
        outData.MeshID = meshID;
        outData.InstanceID = meshInstanceID;
        outData.FaceID = faceID;
        outData.MeshletsStart = workgroupFirstMeshlet;
        
        uint survivingCount = subgroupBallotBitCount(visibleMeshletsBitmask);
        gl_TaskCountNV = survivingCount;
    }
}