#version 460 core
#extension GL_ARB_bindless_texture : require
layout(local_size_x = 12, local_size_y = 6, local_size_z = 1) in;

struct Frustum
{
	vec4 Planes[6];
};

struct DrawCommand
{
    int Count;
    int InstanceCount;
    int FirstIndex;
    int BaseVertex;
    int BaseInstance;
};

struct Node
{
    vec3 Min;
    uint VerticesStart;
    vec3 Max;
    uint VertexCount;
    vec3 _pad0;
    uint MissLink;
};

struct Mesh
{
    int InstanceCount;
    int MatrixStart;
    int NodeStart;
    int BLASDepth;
    int MaterialIndex;
    float Emissive;
    float NormalMapStrength;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
};

struct PointShadow
{
    samplerCubeShadow Sampler;
    float NearPlane;
    float FarPlane;

    mat4 ProjViewMatrices[6];

    vec3 _pad0;
    int LightIndex;
};

layout(std430, binding = 0) restrict writeonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandsSSBO;

layout(std430, binding = 1) restrict readonly buffer BVHSSBO
{
    Node Nodes[];
} bvhSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 4) restrict readonly buffer MatrixSSBO
{
    mat4 Models[];
} matrixSSBO;

layout(std140, binding = 2) uniform ShadowDataUBO
{
    #define GLSL_MAX_UBO_POINT_SHADOW_COUNT 3 // used in shader and client code - keep in sync!
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

Frustum ExtractFrustum(mat4 projViewModel);
bool AABBVsFrustum(Frustum frustum, Node node);
vec3 NegativeVertex(Node node, vec3 normal);

layout(location = 0) uniform int ShadowIndex;

// 1. Count number of shadow-cubemap-faces the mesh is visible from the shadow source
// 2. Pack each visible face into a single int
// 3. Write the packed int into the BaseInstance draw command paramter. The shadow vertex shader will access this variable
// 4. Also write the InstanceCount into the draw command buffer - one instance for each mesh

// Note: Meshes are processed in batches of LOCAL_SIZE_X Threads. Additionaly each mesh gets processed by 6 Threads one for each face.

shared int SharedPackedValues[gl_WorkGroupSize.x];
shared int SharedInstanceCounts[gl_WorkGroupSize.x];
void main()
{
    uint globalMeshIndex = gl_GlobalInvocationID.x;
    if (globalMeshIndex >= meshSSBO.Meshes.length())
        return;

    int cubemapFace = int(gl_LocalInvocationID.y);
    int localMeshIndex = int(gl_LocalInvocationID.x);

    SharedPackedValues[localMeshIndex] = 0;
    SharedInstanceCounts[localMeshIndex] = 0;

    Mesh mesh = meshSSBO.Meshes[globalMeshIndex];
    Node node = bvhSSBO.Nodes[mesh.NodeStart];
    const int glInstanceID = 0; // TODO: Derive from built in variables
    mat4 model = matrixSSBO.Models[mesh.MatrixStart + glInstanceID];
    Frustum frustum = ExtractFrustum(shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[cubemapFace] * model);

    barrier();
    if (AABBVsFrustum(frustum, node))
    {
        // Basically a atomic bitfieldInsert()
        atomicOr(SharedPackedValues[localMeshIndex], cubemapFace << (3 * atomicAdd(SharedInstanceCounts[localMeshIndex], 1)));
    }

    barrier();
    if (cubemapFace == 0)
    {
        drawCommandsSSBO.DrawCommands[globalMeshIndex].InstanceCount = SharedInstanceCounts[localMeshIndex];
        drawCommandsSSBO.DrawCommands[globalMeshIndex].BaseInstance = SharedPackedValues[localMeshIndex];
    }
}

Frustum ExtractFrustum(mat4 projViewModel)
{
    Frustum frustum;
	for (int i = 0; i < 3; i++)
    {
        for (int j = 0; j < 2; j++)
        {
            frustum.Planes[i * 2 + j].x = projViewModel[0][3] + (j == 0 ? projViewModel[0][i] : -projViewModel[0][i]);
            frustum.Planes[i * 2 + j].y = projViewModel[1][3] + (j == 0 ? projViewModel[1][i] : -projViewModel[1][i]);
            frustum.Planes[i * 2 + j].z = projViewModel[2][3] + (j == 0 ? projViewModel[2][i] : -projViewModel[2][i]);
            frustum.Planes[i * 2 + j].w = projViewModel[3][3] + (j == 0 ? projViewModel[3][i] : -projViewModel[3][i]);
            frustum.Planes[i * 2 + j] *= length(frustum.Planes[i * 2 + j].xyz);
        }
    }
	return frustum;
}

bool AABBVsFrustum(Frustum frustum, Node node)
{
	float a = 1.0;

	for (int i = 0; i < 6 && a >= 0.0; i++) {
		vec3 negative = NegativeVertex(node, frustum.Planes[i].xyz);

		a = dot(vec4(negative, 1.0), frustum.Planes[i]);
	}

	return a >= 0.0;
}

vec3 NegativeVertex(Node node, vec3 normal)
{
	return mix(node.Min, node.Max, greaterThan(normal, vec3(0.0)));
}