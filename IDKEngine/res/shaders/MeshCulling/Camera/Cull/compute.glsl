#version 460 core
#extension GL_ARB_bindless_texture : require

#define TAKE_MESH_SHADER_PATH AppInsert(TAKE_MESH_SHADER_PATH)
AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct DrawElementsCmd
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float TransmissionBias;
    float IORBias;
    uint MeshletsStart;
    vec3 AbsorbanceBias;
    uint MeshletCount;
    uint InstanceCount;
    uint BlasRootNodeIndex;
    vec2 _pad0;
};

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
    vec3 _pad0;
    uint MeshIndex;
};

struct BlasNode
{
    vec3 Min;
    uint TriStartOrChild;
    vec3 Max;
    uint TriCount;
};

struct MeshletTaskCmd
{
    uint Count;
    uint First;
};

layout(std430, binding = 0) restrict buffer DrawElementsCmdSSBO
{
    DrawElementsCmd DrawCommands[];
} drawElementsCmdSSBO;

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2, row_major) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 3) restrict writeonly buffer VisibleMeshInstanceSSBO
{
    uint MeshInstanceIDs[];
} visibleMeshInstanceSSBO;

layout(std430, binding = 5) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std430, binding = 13) restrict writeonly buffer MeshletTaskCmdSSBO
{
    MeshletTaskCmd TaskCommands[];
} meshletTaskCmdSSBO;

layout(std430, binding = 14) restrict writeonly buffer MeshletTasksCountSSBO
{
    uint Count;
} meshletTasksCountSSBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    uint Frame;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaRenderTime;
    float Time;
} basicDataUBO;

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
    
    mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
    mat4 prevModelMatrix = mat4(meshInstance.PrevModelMatrix);

    bool isVisible = true;

    Box meshLocalBounds = Box(node.Min, node.Max);

    Frustum frustum = GetFrustum(basicDataUBO.ProjView * modelMatrix);
    isVisible = FrustumBoxIntersect(frustum, meshLocalBounds);

    // Occlusion cull
    const bool doHiZCulling = false;
    if (isVisible && doHiZCulling)
    {
        bool vertexBehindFrustum;
        Box meshletOldNdcBounds = BoxTransformPerspective(meshLocalBounds, basicDataUBO.PrevProjView * prevModelMatrix, vertexBehindFrustum);
        if (!vertexBehindFrustum)
        {
            isVisible = BoxDepthBufferIntersect(meshletOldNdcBounds, gBufferDataUBO.Depth);
        }
    }

    if (isVisible)
    {
    #if TAKE_MESH_SHADER_PATH

        uint meshletTaskID = atomicAdd(meshletTasksCountSSBO.Count, 1u);
        visibleMeshInstanceSSBO.MeshInstanceIDs[meshletTaskID] = meshInstanceID;
        
        const uint taskShaderWorkGroupSize = 32;
        uint meshletCount = meshSSBO.Meshes[meshID].MeshletCount;
        uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
        meshletTaskCmdSSBO.TaskCommands[meshletTaskID].Count = meshletsWorkGroupCount;

    #else

        uint index = atomicAdd(drawElementsCmdSSBO.DrawCommands[meshID].InstanceCount, 1u);
        visibleMeshInstanceSSBO.MeshInstanceIDs[drawCmd.BaseInstance + index] = meshInstanceID;

    #endif

    }
}