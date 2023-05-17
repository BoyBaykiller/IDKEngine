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

vec3 NegativeVertex(BlasNode node, vec3 normal)
{
	return mix(node.Min, node.Max, greaterThan(normal, vec3(0.0)));
}

bool FrustumAABBIntersect(Frustum frustum, BlasNode node)
{
	float a = 1.0;

	for (int i = 0; i < 6 && a >= 0.0; i++)
    {
		vec3 negative = NegativeVertex(node, frustum.Planes[i].xyz);
		a = dot(vec4(negative, 1.0), frustum.Planes[i]);
	}

	return a >= 0.0;
}