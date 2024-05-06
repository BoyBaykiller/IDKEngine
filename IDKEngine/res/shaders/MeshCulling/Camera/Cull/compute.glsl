#version 460 core

#define TAKE_MESH_SHADER_PATH_CAMERA AppInsert(TAKE_MESH_SHADER_PATH_CAMERA)
#if TAKE_MESH_SHADER_PATH_CAMERA
    #extension GL_NV_gpu_shader5 : enable
    #define DECLARE_MESHLET_STORAGE_BUFFERS
#endif

#define IS_HI_Z_CULLING AppInsert(IS_HI_Z_CULLING)

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

void main()
{
    uint meshInstanceID = gl_GlobalInvocationID.x;
    if (meshInstanceID >= meshInstanceSSBO.MeshInstances.length())
    {
        return;
    }

    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceID];
    uint meshID = meshInstance.MeshIndex;

    BlasNode node = blasSSBO.Nodes[meshSSBO.Meshes[meshID].BlasRootNodeIndex];
    
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

        uint meshletTaskID = atomicAdd(meshletTasksCountSSBO.Count, 1u);
        visibleMeshInstanceSSBO.MeshInstanceIDs[meshletTaskID] = meshInstanceID;
        
        const uint taskShaderWorkGroupSize = 32;
        uint meshletCount = meshSSBO.Meshes[meshID].MeshletCount;
        uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
        meshletTaskCmdSSBO.TaskCommands[meshletTaskID].Count = meshletsWorkGroupCount;

    #else

        DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshID];

        uint index = atomicAdd(drawElementsCmdSSBO.DrawCommands[meshID].InstanceCount, 1u);
        visibleMeshInstanceSSBO.MeshInstanceIDs[drawCmd.BaseInstance + index] = meshInstanceID;

    #endif

    }
}