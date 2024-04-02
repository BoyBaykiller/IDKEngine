#version 460 core

#define TAKE_MESH_SHADER_PATH_SHADOW AppInsert(TAKE_MESH_SHADER_PATH_SHADOW)
#if TAKE_MESH_SHADER_PATH_SHADOW
    #extension GL_NV_gpu_shader5 : enable
    #define DECLARE_MESHLET_STORAGE_BUFFERS
#endif

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int NumVisibleFaces;
layout(location = 2) uniform uint VisibleFaces;

void main()
{
    uint meshInstanceID = gl_GlobalInvocationID.x;
    if (meshInstanceID >= meshInstanceSSBO.MeshInstances.length())
    {
        return;
    }

    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceID];
    uint meshID = meshInstance.MeshIndex;
    
    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshID];
    BlasNode node = blasSSBO.Nodes[meshSSBO.Meshes[meshID].BlasRootNodeIndex];

    for (int i = 0; i < NumVisibleFaces; i++)
    {
        uint faceID = (VisibleFaces >> (i * 3)) & ((1u << 3) - 1);

        mat4 projView = shadowsUBO.PointShadows[ShadowIndex].ProjViewMatrices[faceID];
        mat4 modelMatrix = mat4(meshInstance.ModelMatrix);

        Frustum frustum = GetFrustum(projView * modelMatrix);
        bool isVisible = FrustumBoxIntersect(frustum, Box(node.Min, node.Max));

        uint faceAndMeshInstanceID = (faceID << 29) | meshInstanceID; // 3bits | 29bits

        if (isVisible)
        {
        #if TAKE_MESH_SHADER_PATH_SHADOW

            uint meshletTaskID = atomicAdd(meshletTasksCountSSBO.Count, 1u);
            visibleMeshInstanceSSBO.MeshInstanceIDs[meshletTaskID] = faceAndMeshInstanceID;
            
            const uint taskShaderWorkGroupSize = 32;
            uint meshletCount = meshSSBO.Meshes[meshID].MeshletCount;
            uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
            meshletTaskCmdSSBO.TaskCommands[meshletTaskID].Count = meshletsWorkGroupCount;

        #else

            uint index = atomicAdd(drawElementsCmdSSBO.DrawCommands[meshID].InstanceCount, 1u);
            visibleMeshInstanceSSBO.MeshInstanceIDs[drawCmd.BaseInstance * 6 + index] = faceAndMeshInstanceID;

        #endif
        }
    }
}