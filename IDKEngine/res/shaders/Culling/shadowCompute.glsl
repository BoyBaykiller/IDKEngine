#version 460 core
#extension GL_ARB_bindless_texture : require

layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;

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
    uint IsLeafAndVerticesStart;
    vec3 Max;
    uint MissLinkAndVerticesCount;
};

struct Mesh
{
    mat4 Model;
    mat4 PrevModel;
    int MaterialIndex;
    int BaseNode;
    int _pad0;
    int _pad1;
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

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[8];
    int PointCount;
} shadowDataUBO;

Frustum ExtractFrustum(mat4 projViewModel);
bool AABBVsFrustum(Frustum frustum, Node node);
vec3 NegativeVertex(Node node, vec3 normal);
void Pack3BitValue(int threeBitValue, int index, inout int dest);

layout(location = 0) uniform int ShadowIndex;

void main()
{
    const uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
        return;

    Mesh mesh = meshSSBO.Meshes[meshIndex];
    Node node = bvhSSBO.Nodes[mesh.BaseNode];
    PointShadow pointShadow = shadowDataUBO.PointShadows[ShadowIndex];

    int instances = 0;
    int packedValue = 0;
    // TODO: Parallelize this for loop over 6 threads
    for (int i = 0; i < 6; i++)
    {
        Frustum frustum = ExtractFrustum(pointShadow.ProjViewMatrices[i] * mesh.Model);
        if (AABBVsFrustum(frustum, node))
        {
            Pack3BitValue(i, instances++, packedValue);
        }
    }
    drawCommandsSSBO.DrawCommands[meshIndex].InstanceCount = instances;
    drawCommandsSSBO.DrawCommands[meshIndex].BaseInstance = packedValue;
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

void Pack3BitValue(int threeBitValue, int index, inout int dest)
{
    dest |= threeBitValue << (3 * index);
}
