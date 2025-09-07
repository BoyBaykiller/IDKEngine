#version 460 core

#define TAKE_MESH_SHADER_PATH_CAMERA AppInsert(TAKE_MESH_SHADER_PATH_CAMERA)
#if TAKE_MESH_SHADER_PATH_CAMERA
    #extension GL_NV_gpu_shader5 : enable
    #define DECLARE_MESHLET_STORAGE_BUFFERS
    #define DECLARE_MESHLET_RENDERING_TYPES
#endif

#define IS_HI_Z_CULLING AppInsert(IS_HI_Z_CULLING)

AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(std430, binding = 0) restrict readonly buffer InCullingMeshInstanceIdSSBO
{
    uint Ids[];
} inCullingMeshInstanceIdSSBO;

void main()
{
    if (gl_GlobalInvocationID.x >= inCullingMeshInstanceIdSSBO.Ids.length())
    {
        return;
    }

    uint meshInstanceId = inCullingMeshInstanceIdSSBO.Ids[gl_GlobalInvocationID.x];

    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceId];
    uint meshId = meshInstance.MeshId;

    GpuBlasNode node = blasNodeSSBO.Nodes[blasDescSSBO.Descs[meshId].RootNodeOffset + 1];
    
    mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
    mat4 prevModelMatrix = mat4(meshInstance.PrevModelMatrix);

    bool isVisible = true;

    Box meshLocalBounds = Box(node.Min, node.Max);

    Frustum frustum = GetFrustum(perFrameDataUBO.ProjView * modelMatrix);
    isVisible = FrustumBoxIntersect(frustum, meshLocalBounds);

#if IS_HI_Z_CULLING
    if (isVisible)
    {
        // Occlusion Culling
        bool vertexBehindFrustum;
        Box meshletOldNdcBounds = BoxTransformPerspective(meshLocalBounds, perFrameDataUBO.PrevProjView * prevModelMatrix, vertexBehindFrustum);
        if (!vertexBehindFrustum)
        {
            isVisible = BoxDepthBufferIntersect(meshletOldNdcBounds, gBufferDataUBO.Depth);
        }
    }
#endif

    if (isVisible)
    {
    #if TAKE_MESH_SHADER_PATH_CAMERA

        uint meshletTaskId = atomicAdd(meshletTasksCountSSBO.Count, 1u);
        visibleMeshInstanceIdSSBO.Ids[meshletTaskId] = meshInstanceId;
        
        const uint taskShaderWorkGroupSize = 32;
        uint meshletCount = meshSSBO.Meshes[meshId].MeshletCount;
        uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
        meshletTaskCmdSSBO.Commands[meshletTaskId].Count = meshletsWorkGroupCount;

    #else

        GpuDrawElementsCmd drawCmd = drawElementsCmdSSBO.Commands[meshId];

        uint index = atomicAdd(drawElementsCmdSSBO.Commands[meshId].InstanceCount, 1u);
        visibleMeshInstanceIdSSBO.Ids[drawCmd.BaseInstance + index] = meshInstanceId;

    #endif

    }
}