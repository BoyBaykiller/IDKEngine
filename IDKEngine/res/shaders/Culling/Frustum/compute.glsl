#version 460 core

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct Frustum
{
    vec4 Planes[6];
};

AppInclude(shaders/include/Buffers.glsl)

vec3 NegativeVertex(Node node, vec3 normal);
bool FrustumAABBIntersect(Frustum frustum, Node node);
Frustum ExtractFrustum(mat4 matrix);

layout(location = 0) uniform mat4 ProjView;

void main()
{
    uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
        return;

    DrawCommand meshCMD = drawCommandSSBO.DrawCommands[meshIndex];
    Node node = blasSSBO.Nodes[2 * (meshCMD.FirstIndex / 3)];
    
    const uint glInstanceID = 0;  // TODO: Derive from built in variables
    mat4 model = meshInstanceSSBO.MeshInstances[meshCMD.BaseInstance + glInstanceID].ModelMatrix;
    
    Frustum frustum = ExtractFrustum(ProjView * model);
    drawCommandSSBO.DrawCommands[meshIndex].InstanceCount = int(FrustumAABBIntersect(frustum, node));
}

Frustum ExtractFrustum(mat4 matrix)
{
    Frustum frustum;
	for (int i = 0; i < 3; i++)
    {
        for (int j = 0; j < 2; j++)
        {
            frustum.Planes[i * 2 + j].x = matrix[0][3] + (j == 0 ? matrix[0][i] : -matrix[0][i]);
            frustum.Planes[i * 2 + j].y = matrix[1][3] + (j == 0 ? matrix[1][i] : -matrix[1][i]);
            frustum.Planes[i * 2 + j].z = matrix[2][3] + (j == 0 ? matrix[2][i] : -matrix[2][i]);
            frustum.Planes[i * 2 + j].w = matrix[3][3] + (j == 0 ? matrix[3][i] : -matrix[3][i]);
            frustum.Planes[i * 2 + j] *= length(frustum.Planes[i * 2 + j].xyz);
        }
    }
	return frustum;
}

bool FrustumAABBIntersect(Frustum frustum, Node node)
{
    float a = 1.0;

    for (int i = 0; i < 6; i++)
    {
        vec3 negative = NegativeVertex(node, frustum.Planes[i].xyz);
        a = dot(vec4(negative, 1.0), frustum.Planes[i]);

        if (a < 0.0)
        {
            return false;
        }
    }

    return true;
}

vec3 NegativeVertex(Node node, vec3 normal)
{
	return mix(node.Min, node.Max, greaterThan(normal, vec3(0.0)));
}
