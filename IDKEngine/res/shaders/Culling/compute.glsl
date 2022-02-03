#version 460 core

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
    int VerticesStart;
    vec3 Max;
    int VerticesEnd;
};

struct Mesh
{
    mat4 Model[1];
    int MaterialIndex;
    int BaseNode;
    int _pad0;
    int _pad1;
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

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

layout(location = 0) uniform int MeshCount;

vec3 NegativeVertex(Node node, vec3 normal);
bool AABBVsFrustum(Frustum frustum, Node node);
Frustum ExtractFrustum(mat4 projView);

void main()
{
    if (gl_GlobalInvocationID.x >= MeshCount)
        return;

    Mesh mesh = meshSSBO.Meshes[gl_GlobalInvocationID.x];
    Node node = bvhSSBO.Nodes[mesh.BaseNode];
    
    Frustum frustum = ExtractFrustum(basicDataUBO.ProjView * mesh.Model[0]);
    drawCommandsSSBO.DrawCommands[gl_GlobalInvocationID.x].InstanceCount = int(AABBVsFrustum(frustum, node));
}

Frustum ExtractFrustum(mat4 projView)
{
    Frustum frustum;
	for (int i = 0; i < 3; ++i)
    {
        for (int j = 0; j < 2; ++j)
        {
            frustum.Planes[i * 2 + j].x = projView[0][3] + (j == 0 ? projView[0][i] : -projView[0][i]);
            frustum.Planes[i * 2 + j].y = projView[1][3] + (j == 0 ? projView[1][i] : -projView[1][i]);
            frustum.Planes[i * 2 + j].z = projView[2][3] + (j == 0 ? projView[2][i] : -projView[2][i]);
            frustum.Planes[i * 2 + j].w = projView[3][3] + (j == 0 ? projView[3][i] : -projView[3][i]);
            frustum.Planes[i * 2 + j] *= length(frustum.Planes[i * 2 + j].xyz);
        }
    }
	return frustum;
}

bool AABBVsFrustum(Frustum frustum, Node node)
{
	float a = 1.0;

	for (int i = 0; i < 6 && a >= 0.0; ++i) {
		vec3 negative = NegativeVertex(node, frustum.Planes[i].xyz);

		a = dot(vec4(negative, 1.0), frustum.Planes[i]);
	}

	return a >= 0.0;
}

vec3 NegativeVertex(Node node, vec3 normal)
{
	return mix(node.Min, node.Max, greaterThan(normal, vec3(0.0)));
}
