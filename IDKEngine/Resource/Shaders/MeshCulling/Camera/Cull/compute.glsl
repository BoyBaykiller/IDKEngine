#version 460 core

#define TAKE_MESH_SHADER_PATH_CAMERA AppInsert(TAKE_MESH_SHADER_PATH_CAMERA)
#if TAKE_MESH_SHADER_PATH_CAMERA
    #extension GL_NV_gpu_shader5 : require
    #define DECLARE_MESHLET_STORAGE_BUFFERS
    #define DECLARE_MESHLET_RENDERING_TYPES
#endif

#define IS_HI_Z_CULLING AppInsert(IS_HI_Z_CULLING)

AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(std430, binding = 0) restrict readonly buffer MeshInstanceIdSSBO
{
    uint Ids[];
} meshInstanceIdSSBO;

void main()
{
    if (gl_GlobalInvocationID.x >= meshInstanceIdSSBO.Ids.length())
    {
        return;
    }

    uint meshInstanceId = meshInstanceIdSSBO.Ids[gl_GlobalInvocationID.x];

    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceId];
    GpuMeshTransform meshTransform = meshTransformSSBO.Transforms[meshInstance.MeshTransformId];
    uint meshId = meshInstance.MeshId;

    GpuMesh mesh = meshSSBO.Meshes[meshId];
    Box localBounds = Box(mesh.LocalBoundsMin, mesh.LocalBoundsMax);
    
    mat4 modelMatrix = mat4(meshTransform.ModelMatrix);
    mat4 prevModelMatrix = mat4(meshTransform.PrevModelMatrix);

    bool isVisible = true;

    Frustum frustum = GetFrustum(perFrameDataUBO.ProjView * modelMatrix);
    isVisible = FrustumBoxIntersect(frustum, localBounds);

#if IS_HI_Z_CULLING
    if (isVisible)
    {
        // Occlusion Culling
        bool vertexBehindFrustum;
        Box meshletOldNdcBounds = BoxTransformPerspective(localBounds, perFrameDataUBO.PrevProjView * prevModelMatrix, vertexBehindFrustum);
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
        uint meshletCount = mesh.MeshletCount;
        uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
        meshletTaskCmdSSBO.Commands[meshletTaskId].Count = meshletsWorkGroupCount;

    #else

        GpuDrawElementsCmd drawCmd = drawElementsCmdSSBO.Commands[meshId];

        uint index = atomicAdd(drawElementsCmdSSBO.Commands[meshId].InstanceCount, 1u);
        visibleMeshInstanceIdSSBO.Ids[drawCmd.BaseInstance + index] = meshInstanceId;

    #endif

    }
}