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

struct PointShadow
{
    samplerCube Texture;
    samplerCubeShadow ShadowTexture;

    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    vec3 _pad0;
    float FarPlane;
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

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
} shadowDataUBO;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int FaceIndex;

void main()
{
    uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
    {
        return;
    }

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshIndex];
    BlasNode node = blasSSBO.Nodes[drawCmd.BlasRootNodeIndex];

    mat4 projView = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[FaceIndex];
    mat4 modelMatrix = mat4(meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance].ModelMatrix);

    Frustum frustum = GetFrustum(projView * modelMatrix);
    bool isVisible = FrustumBoxIntersect(frustum, Box(node.Min, node.Max));

    // For vertex rendering path
    drawElementsCmdSSBO.DrawCommands[meshIndex].InstanceCount = isVisible ? 1 : 0;

    // For mesh shader rendering path
    uint meshletCount = meshSSBO.Meshes[meshIndex].MeshletCount;
    const uint taskShaderWorkGroupSize = 32;
    uint meshletsWorkGroupCount = (meshletCount + taskShaderWorkGroupSize - 1) / taskShaderWorkGroupSize;
    meshletTaskCmdSSBO.TaskCommands[meshIndex].Count = isVisible ? meshletsWorkGroupCount : 0;
}