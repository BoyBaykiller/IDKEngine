AppInclude(include/Ray.glsl)
AppInclude(include/Box.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/Frustum.glsl)

bool RayTriangleIntersect(Ray ray, vec3 p0, vec3 p1, vec3 p2, out vec3 bary, out float t)
{
    // Source: https://www.iquilezles.org/www/articles/intersectors/intersectors.htm

    vec3 p1p0 = p1 - p0;
    vec3 p2p0 = p2 - p0;
    vec3 rop0 = ray.Origin - p0;
    vec3 normal = cross(p1p0, p2p0);
    vec3 q = cross(rop0, ray.Direction);

    float x = dot(ray.Direction, normal);
    t = dot(-normal, rop0) / x;

    bary.yz = vec2(dot(-q, p2p0), dot(q, p1p0)) / x;
    bary.x = 1.0 - bary.y - bary.z;

    return all(greaterThanEqual(vec4(bary, t), vec4(0.0)));
}

bool RayBoxIntersect(Ray ray, Box box, out float t1, out float t2)
{
    // Source: https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525

    t1 = FLOAT_MIN;
    t2 = FLOAT_MAX;

    vec3 t0s = (box.Min - ray.Origin) / ray.Direction;
    vec3 t1s = (box.Max - ray.Origin) / ray.Direction;

    vec3 tsmaller = min(t0s, t1s);
    vec3 tbigger = max(t0s, t1s);

    t1 = max(t1, max(tsmaller.x, max(tsmaller.y, max(tsmaller.z, 0.0))));
    t2 = min(t2, min(tbigger.x, min(tbigger.y, tbigger.z)));

    return t1 <= t2;
}

bool RayBoxIntersect(Ray ray, Box box, out float t1)
{
    float t2;
    return RayBoxIntersect(ray, box, t1, t2);
}

bool RaySphereIntersect(Ray ray, vec3 position, float radius, out float t1, out float t2)
{
    // Source: https://antongerdelan.net/opengl/raycasting.html
    
    t1 = FLOAT_MAX;
    t2 = FLOAT_MAX;

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

bool FrustumBoxIntersect(Frustum frustum, Box box)
{
    for (int i = 0; i < 6; i++)
    {
        vec3 negative = mix(box.Min, box.Max, greaterThan(frustum.Planes[i].xyz, vec3(0.0)));
        float a = dot(vec4(negative, 1.0), frustum.Planes[i]);

        if (a < 0.0)
        {
            return false;
        }
    }

    return true;
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

bool BoxDepthBufferIntersect(Box geometryBoxNdc, sampler2D samplerHiZ)
{
    // https://interplayoflight.wordpress.com/2017/11/15/experiments-in-gpu-based-occlusion-culling/

    vec2 boxUvMin = clamp(geometryBoxNdc.Min.xy * 0.5 + 0.5, vec2(0.0), vec2(1.0));
    vec2 boxUvMax = clamp(geometryBoxNdc.Max.xy * 0.5 + 0.5, vec2(0.0), vec2(1.0));

    vec2 scale = textureSize(samplerHiZ, 0);
    uvec2 pMin = uvec2(floor(boxUvMin * scale));
    uvec2 pMax = uvec2(ceil(boxUvMax * scale));
    uvec2 size = pMax - pMin;
    int level = min(CeilLog2Int(max(size.x, size.y)), textureQueryLevels(samplerHiZ) - 1);

    // If possible refine the level by minus one to be less conservative
    int lowerLevel = max(level - 1, 0);
    scale = textureSize(samplerHiZ, lowerLevel);
    pMin = ivec2(floor(boxUvMin * scale));
    pMax = ivec2(ceil(boxUvMax * scale));
    size = pMax - pMin;
    if (size.x <= 2 && size.y <= 2)
    {
        level = lowerLevel;
    }

    vec4 depths;
    depths.x = textureLod(samplerHiZ, boxUvMin, level).r;
    depths.y = textureLod(samplerHiZ, vec2(boxUvMax.x, boxUvMin.y), level).r;
    depths.z = textureLod(samplerHiZ, vec2(boxUvMin.x, boxUvMax.y), level).r;
    depths.w = textureLod(samplerHiZ, boxUvMax, level).r;

    float furthestDepth = max(max(depths.x, depths.y), max(depths.z, depths.w));
    float closestBoxDepth = clamp(geometryBoxNdc.Min.z, 0.0, 1.0);
    bool isVisible = closestBoxDepth <= furthestDepth;

    return isVisible;
}
