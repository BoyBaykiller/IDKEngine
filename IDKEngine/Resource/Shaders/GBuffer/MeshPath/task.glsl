#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_KHR_shader_subgroup_ballot : require

#define IS_HI_Z_CULLING AppInsert(IS_HI_Z_CULLING)

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
    uint InstanceID;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[MESHLETS_PER_WORKGROUP];
} outData;

void main()
{
    uint meshInstanceID = visibleMeshInstanceSSBO.MeshInstanceIDs[gl_DrawID];
    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceID];

    uint meshID = meshInstance.MeshId;
    GpuMesh mesh = meshSSBO.Meshes[meshID];

    if (gl_GlobalInvocationID.x >= mesh.MeshletCount)
    {
        return;
    }

    uint8_t localMeshlet = uint8_t(gl_LocalInvocationIndex);
    uint workgroupFirstMeshlet = mesh.MeshletsStart + (gl_WorkGroupID.x * MESHLETS_PER_WORKGROUP);
    uint workgroupThisMeshlet = workgroupFirstMeshlet + localMeshlet;

    GpuDrawElementsCmd drawCmd = drawElementsCmdSSBO.Commands[meshID];
    GpuMeshletInfo meshletInfo = meshletInfoSSBO.MeshletsInfo[workgroupThisMeshlet];

    mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
    mat4 prevModelMatrix = mat4(meshInstance.PrevModelMatrix);
    
    bool isVisible = true;

    Box meshletLocalBounds = Box(meshletInfo.Min, meshletInfo.Max);
    
    // // Subpixel culling
    // {
    //     bool vertexBehindFrustum;
    //     Box meshletNdcBounds = BoxTransformPerspective(meshletLocalBounds, perFrameDataUBO.ProjView * modelMatrix, vertexBehindFrustum);
    //     vec2 renderSize = textureSize(gBufferDataUBO.AlbedoAlpha, 0);
    //     vec3 uvMin = vec3(meshletNdcBounds.Min.xy * 0.5 + 0.5, meshletNdcBounds.Min.z);
    //     vec3 uvMax = vec3(meshletNdcBounds.Max.xy * 0.5 + 0.5, meshletNdcBounds.Max.z);
    //     ivec2 size = ivec2(ceil((uvMax.xy - uvMin.xy) * renderSize));
    //     if (size.x < 1 || size.y < 1)
    //     {
    //         isVisible = false;
    //     }
    // }

    // Frustum Culling
    if (isVisible)
    {
        Frustum frustum = GetFrustum(perFrameDataUBO.ProjView * modelMatrix);
        isVisible = FrustumBoxIntersect(frustum, meshletLocalBounds);
    }

#if IS_HI_Z_CULLING
    // Hi-Z Occlusion Culling
    if (isVisible)
    {
        bool vertexBehindFrustum;
        Box meshletOldNdcBounds = BoxTransformPerspective(meshletLocalBounds, perFrameDataUBO.PrevProjView * prevModelMatrix, vertexBehindFrustum);
        if (!vertexBehindFrustum)
        {
            isVisible = BoxDepthBufferIntersect(meshletOldNdcBounds, gBufferDataUBO.Depth);
        }
    }
#endif

    uvec4 visibleMeshletsBitmask = subgroupBallot(isVisible);
    if (isVisible)
    {
        uint offset = subgroupBallotExclusiveBitCount(visibleMeshletsBitmask);
        outData.SurvivingMeshlets[offset] = localMeshlet;
    }

    if (gl_LocalInvocationIndex == 0)
    {
        outData.MeshId = meshID;
        outData.InstanceID = meshInstanceID;
        outData.MeshletsStart = workgroupFirstMeshlet;
        
        uint survivingCount = subgroupBallotBitCount(visibleMeshletsBitmask);
        gl_TaskCountNV = survivingCount;
    }
}