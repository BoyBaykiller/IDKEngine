#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)

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

layout(std430, binding = 0) restrict buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandSSBO;

layout(std430, binding = 1) restrict writeonly buffer MeshSSBO
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

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

layout(location = 0) uniform int ShadowIndex;

AppInclude(Culling/Frustum/include/common.glsl)

// 1. Count number of shadow-cubemap-faces the mesh is visible from the shadow source
// 2. Pack each visible face into a single int
// 3. Write the packed int into a global variable. The shadow vertex shader will access this variable
// 4. Also write the InstanceCount into the draw command buffer - one instance for each mesh
void main()
{
    uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
        return;

    DrawCommand drawCmd = drawCommandSSBO.DrawCommands[meshIndex];
    BlasNode node = blasSSBO.Nodes[2 * (drawCmd.FirstIndex / 3)];
    PointShadow pointShadow = shadowDataUBO.PointShadows[ShadowIndex];

    int instances = 0;
    int packedValue = 0;
    mat4 model = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance].ModelMatrix;

    for (int i = 0; i < 6; i++)
    {
        Frustum frustum = ExtractFrustum(pointShadow.ProjViewMatrices[i] * model);
        if (FrustumAABBIntersect(frustum, node))
        {
            packedValue = bitfieldInsert(packedValue, i, 3 * instances++, 3);
        }
    }
    drawCommandSSBO.DrawCommands[meshIndex].InstanceCount = instances;
    meshSSBO.Meshes[meshIndex].CubemapShadowCullInfo = packedValue;
}
