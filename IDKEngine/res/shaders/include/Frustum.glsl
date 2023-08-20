#ifndef Frustum_h
#define Frustum_h

struct Frustum
{
    vec4 Planes[6];
};

Frustum FrustumExtract(mat4 view)
{
    Frustum frustum;
    for (int i = 0; i < 3; i++)
    {
        for (int j = 0; j < 2; j++)
        {
            frustum.Planes[i * 2 + j].x = view[0][3] + (j == 0 ? view[0][i] : -view[0][i]);
            frustum.Planes[i * 2 + j].y = view[1][3] + (j == 0 ? view[1][i] : -view[1][i]);
            frustum.Planes[i * 2 + j].z = view[2][3] + (j == 0 ? view[2][i] : -view[2][i]);
            frustum.Planes[i * 2 + j].w = view[3][3] + (j == 0 ? view[3][i] : -view[3][i]);
            frustum.Planes[i * 2 + j] *= length(frustum.Planes[i * 2 + j].xyz);
        }
    }
    return frustum;
}

bool FrustumBoxIntersect(Frustum frustum, vec3 boxMin, vec3 boxMax)
{
	float a = 1.0;
	for (int i = 0; i < 6 && a >= 0.0; i++)
    {
		vec3 negative = mix(boxMin, boxMax, greaterThan(frustum.Planes[i].xyz, vec3(0.0)));
		a = dot(vec4(negative, 1.0), frustum.Planes[i]);
	}

	return a >= 0.0;
}

#endif