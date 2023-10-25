#ifndef Frustum_H
#define Frustum_H

struct Frustum
{
    vec4 Planes[6];
};

Frustum GetFrustum(mat4 matrix)
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