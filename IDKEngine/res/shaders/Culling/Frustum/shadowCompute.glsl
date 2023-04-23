#version 460 core

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct Frustum
{
	vec4 Planes[6];
};

AppInclude(shaders/include/Buffers.glsl)

Frustum ExtractFrustum(mat4 projViewModel);
bool AABBVsFrustum(Frustum frustum, Node node);
vec3 NegativeVertex(Node node, vec3 normal);

layout(location = 0) uniform int ShadowIndex;

// 1. Count number of shadow-cubemap-faces the mesh is visible from the shadow source
// 2. Pack each visible face into a single int
// 3. Write the packed int into a global variable. The shadow vertex shader will access this variable
// 4. Also write the InstanceCount into the draw command buffer - one instance for each mesh
void main()
{
    uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
        return;

    DrawCommand meshCMD = drawCommandSSBO.DrawCommands[meshIndex];
    Node node = blasSSBO.Nodes[2 * (meshCMD.FirstIndex / 3)];
    PointShadow pointShadow = shadowDataUBO.PointShadows[ShadowIndex];

    int instances = 0;
    int packedValue = 0;
    mat4 model = meshInstanceSSBO.MeshInstances[meshCMD.BaseInstance].ModelMatrix;

    for (int i = 0; i < 6; i++)
    {
        Frustum frustum = ExtractFrustum(pointShadow.ProjViewMatrices[i] * model);
        if (AABBVsFrustum(frustum, node))
        {
            packedValue = bitfieldInsert(packedValue, i, 3 * instances++, 3);
        }
    }
    drawCommandSSBO.DrawCommands[meshIndex].InstanceCount = instances;
    meshSSBO.Meshes[meshIndex].CubemapShadowCullInfo = packedValue;
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