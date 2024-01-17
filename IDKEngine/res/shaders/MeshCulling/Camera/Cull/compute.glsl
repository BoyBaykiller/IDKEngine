#version 460 core
#extension GL_ARB_bindless_texture : require

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

    uint BlasRootNodeIndex;
};

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    uint MeshletsStart;
    vec3 Absorbance;
    uint MeshletCount;
};

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
};

struct BlasNode
{
    vec3 Min;
    uint TriStartOrLeftChild;
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

layout(std430, binding = 5) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std430, binding = 11) restrict writeonly buffer MeshletTaskCmdSSBO
{
    MeshletTaskCmd TaskCommands[];
} meshletTaskCmdSSBO;

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
    uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
    {
        return;
    }

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshIndex];
    BlasNode node = blasSSBO.Nodes[drawCmd.BlasRootNodeIndex];

    const uint glInstanceID = 0; // TODO: Derive from built in variables
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance + glInstanceID];
    
    mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
    mat4 prevModelMatrix = mat4(meshInstance.PrevModelMatrix);

    bool isVisible = true;

    Box meshLocalBounds = Box(node.Min, node.Max);

    Frustum frustum = GetFrustum(basicDataUBO.ProjView * modelMatrix);
    isVisible = FrustumBoxIntersect(frustum, meshLocalBounds);

    // Occlusion cull
    const bool doHiZCulling = false;
    bool vertexBehindFrustum;
    Box meshletOldNdcBounds = BoxTransformPerspective(meshLocalBounds, basicDataUBO.PrevProjView * prevModelMatrix, vertexBehindFrustum);
    if (isVisible && !vertexBehindFrustum && doHiZCulling)
    {
        isVisible = BoxDepthBufferIntersect(meshletOldNdcBounds, gBufferDataUBO.Depth);
    }

    // isVisible = true;

    // For vertex rendering path
    drawElementsCmdSSBO.DrawCommands[meshIndex].InstanceCount = isVisible ? 1 : 0;
    
    // For mesh shader rendering path
    uint meshletCount = meshSSBO.Meshes[meshIndex].MeshletCount;
    const uint taskShaderWorkGroupSize = 32;
    uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
    meshletTaskCmdSSBO.TaskCommands[meshIndex].Count = isVisible ? meshletsWorkGroupCount : 0;
}