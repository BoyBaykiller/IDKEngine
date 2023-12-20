#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct DrawElementsCmd
{
    uint Count;
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
    float _pad0;
    vec3 Absorbance;
    uint CubemapShadowCullInfo;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

struct BlasNode
{
    vec3 Min;
    uint TriStartOrLeftChild;
    vec3 Max;
    uint TriCount;
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

layout(std430, binding = 1) restrict readonly writeonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 5) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
} shadowDataUBO;

layout(location = 0) uniform int PointShadowIndex;
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

    const uint glInstanceID = 0; // TODO: Derive from built in variables
    mat4 model = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance + glInstanceID].PrevModelMatrix;

    mat4 projView = shadowDataUBO.PointShadows[PointShadowIndex].ProjViewMatrices[FaceIndex];

    Frustum frustum = GetFrustum(projView * model);
    bool isVisible = FrustumBoxIntersect(frustum, node.Min, node.Max);

    drawElementsCmdSSBO.DrawCommands[meshIndex].InstanceCount = isVisible ? 1 : 0;
}