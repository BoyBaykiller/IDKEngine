#version 460 core

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct Frustum
{
    vec4 Planes[6];
};

struct DrawCommand
{
    uint Count;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct Mesh
{
    int InstanceCount;
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
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

layout(std430, binding = 0) restrict buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandSSBO;

layout(std430, binding = 1) restrict readonly writeonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 4) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(location = 0) uniform mat4 ProjView;

AppInclude(Culling/Frustum/include/common.glsl)

void main()
{
    uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
        return;

    DrawCommand meshCMD = drawCommandSSBO.DrawCommands[meshIndex];
    BlasNode node = blasSSBO.Nodes[2 * (meshCMD.FirstIndex / 3)];
    
    const uint glInstanceID = 0;  // TODO: Derive from built in variables
    mat4 model = meshInstanceSSBO.MeshInstances[meshCMD.BaseInstance + glInstanceID].ModelMatrix;
    
    Frustum frustum = ExtractFrustum(ProjView * model);
    drawCommandSSBO.DrawCommands[meshIndex].InstanceCount = int(FrustumAABBIntersect(frustum, node));
}
