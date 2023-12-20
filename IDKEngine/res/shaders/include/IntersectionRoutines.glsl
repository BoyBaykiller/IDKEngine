#ifndef IntersectionRoutines_H
#define IntersectionRoutines_H

#define IntersectionRoutines_FLOAT_MAX 3.4028235e+38
#define IntersectionRoutines_FLOAT_MIN -IntersectionRoutines_FLOAT_MAX

AppInclude(include/Ray.glsl)
AppInclude(include/Box.glsl)
AppInclude(include/Frustum.glsl)

// Source: https://www.iquilezles.org/www/articles/intersectors/intersectors.htm
bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec3 bary, out float t)
{
    vec3 v1v0 = v1 - v0;
    vec3 v2v0 = v2 - v0;
    vec3 rov0 = ray.Origin - v0;
    vec3 normal = cross(v1v0, v2v0);
    vec3 q = cross(rov0, ray.Direction);

    float x = dot(ray.Direction, normal);
    bary.yz = vec2(dot(-q, v2v0), dot(q, v1v0)) / x;
    bary.x = 1.0 - bary.y - bary.z;

    t = dot(-normal, rov0) / x;

    return all(greaterThanEqual(vec4(bary, t), vec4(0.0)));
}

// Source: https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525
bool RayBoxIntersect(Ray ray, Box box, out float t1, out float t2)
{
    t1 = IntersectionRoutines_FLOAT_MIN;
    t2 = IntersectionRoutines_FLOAT_MAX;

    vec3 t0s = (box.Min - ray.Origin) / ray.Direction;
    vec3 t1s = (box.Max - ray.Origin) / ray.Direction;

    vec3 tsmaller = min(t0s, t1s);
    vec3 tbigger = max(t0s, t1s);

    t1 = max(t1, max(tsmaller.x, max(tsmaller.y, tsmaller.z)));
    t2 = min(t2, min(tbigger.x, min(tbigger.y, tbigger.z)));

    return t1 <= t2 && t2 > 0.0;
}

// Source: https://antongerdelan.net/opengl/raycasting.html
bool RaySphereIntersect(Ray ray, vec3 position, float radius, out float t1, out float t2)
{
    t1 = IntersectionRoutines_FLOAT_MAX;
    t2 = IntersectionRoutines_FLOAT_MAX;

    vec3 sphereToRay = ray.Origin - position;
    float b = dot(ray.Direction, sphereToRay);
    float c = dot(sphereToRay, sphereToRay) - radius * radius;
    float discriminant = b * b - c;
    if (discriminant < 0.0)
    {
        return false;
    }

    float squareRoot = sqrt(discriminant);
    t1 = -b - squareRoot;
    t2 = -b + squareRoot;

    return t1 <= t2 && t2 > 0.0;
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

bool BoxBoxIntersect(Box a, Box b)
{
    return a.Min.x < b.Max.x &&
           a.Min.y < b.Max.y &&
           a.Min.z < b.Max.z &&

           a.Max.x > b.Min.x &&
           a.Max.y > b.Min.y &&
           a.Max.z > b.Min.z;
}

#endif