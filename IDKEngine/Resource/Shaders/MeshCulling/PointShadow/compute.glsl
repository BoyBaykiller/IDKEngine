#version 460 core

#define TAKE_MESH_SHADER_PATH_SHADOW AppInsert(TAKE_MESH_SHADER_PATH_SHADOW)
#if TAKE_MESH_SHADER_PATH_SHADOW
    #extension GL_NV_gpu_shader5 : enable
    #define DECLARE_MESHLET_STORAGE_BUFFERS
    #define DECLARE_MESHLET_RENDERING_TYPES
#endif

AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(std430, binding = 0) restrict readonly buffer InCullingMeshInstanceIdSSBO
{
    uint Ids[];
} inCullingMeshInstanceIdSSBO;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int NumVisibleFaces;
layout(location = 2) uniform uint VisibleFaces;

void main()
{
    if (gl_GlobalInvocationID.x >= inCullingMeshInstanceIdSSBO.Ids.length())
    {
        return;
    }

    uint meshInstanceId = inCullingMeshInstanceIdSSBO.Ids[gl_GlobalInvocationID.x];

    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceId];
    uint meshId = meshInstance.MeshId;
    
    GpuDrawElementsCmd drawCmd = drawElementsCmdSSBO.Commands[meshId];
    GpuBlasNode node = blasNodeSSBO.Nodes[meshSSBO.Meshes[meshId].BlasRootNodeOffset];

    for (int i = 0; i < NumVisibleFaces; i++)
    {
        uint faceId = (VisibleFaces >> (i * 3)) & ((1u << 3) - 1);

        mat4 projView = shadowsUBO.PointShadows[ShadowIndex].ProjViewMatrices[faceId];
        mat4 modelMatrix = mat4(meshInstance.ModelMatrix);

        Frustum frustum = GetFrustum(projView * modelMatrix);
        bool isVisible = FrustumBoxIntersect(frustum, Box(node.Min, node.Max));

        uint faceAndMeshInstanceId = (faceId << 29) | meshInstanceId; // 3bits | 29bits

        if (isVisible)
        {
        #if TAKE_MESH_SHADER_PATH_SHADOW

            uint meshletTaskId = atomicAdd(meshletTasksCountSSBO.Count, 1u);
            visibleMeshInstanceIdSSBO.Ids[meshletTaskId] = faceAndMeshInstanceId;
            
            const uint taskShaderWorkGroupSize = 32;
            uint meshletCount = meshSSBO.Meshes[meshId].MeshletCount;
            uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
            meshletTaskCmdSSBO.Commands[meshletTaskId].Count = meshletsWorkGroupCount;

        #else

            uint index = atomicAdd(drawElementsCmdSSBO.Commands[meshId].InstanceCount, 1u);
            visibleMeshInstanceIdSSBO.Ids[drawCmd.BaseInstance * 6 + index] = faceAndMeshInstanceId;

        #endif
        }
    }
}