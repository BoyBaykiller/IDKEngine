vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords)
{   
    vec4 rayView;
    rayView.xy = mat2(inverseProj) * normalizedDeviceCoords;
    rayView.z = -1.0;
    rayView.w = 0.0;

    vec3 rayWorld = normalize((inverseView * rayView).xyz);
    return rayWorld;
}

vec3 Interpolate(vec3 v0, vec3 v1, vec3 v2, vec3 bary)
{
    return v0 * bary.x + v1 * bary.y + v2 * bary.z;
}

vec2 Interpolate(vec2 v0, vec2 v1, vec2 v2, vec3 bary)
{
    return v0 * bary.x + v1 * bary.y + v2 * bary.z;
}

float GetLogarithmicDepth(float near, float far, float viewZ)
{
    // Source: https://learnopengl.com/Advanced-OpenGL/Depth-testing
    
    // https://www.desmos.com/calculator/yexmazn9yq
    float depth = (1.0 / viewZ - 1.0 / near) / (1.0 / far - 1.0 / near);
    return depth;
}

float LogarithmicDepthToLinearViewDepth(float near, float far, float ndcZ) 
{
    // https://www.desmos.com/calculator/yexmazn9yq
    float depth = (2.0 * near * far) / (far + near - ndcZ * (far - near));
    return depth;
}

vec3 PerspectiveTransform(vec3 ndc, mat4 matrix)
{
    vec4 worldPos = matrix * vec4(ndc, 1.0);
    return worldPos.xyz / worldPos.w;
}

vec3 PerspectiveTransformUvDepth(vec3 uvAndDepth, mat4 matrix)
{
    vec3 ndc;
    ndc.xy = uvAndDepth.xy * 2.0 - 1.0;
    ndc.z = uvAndDepth.z;
    return PerspectiveTransform(ndc, matrix);
}

float MapRangeToAnOther(float value, float valueMin, float valueMax, float mapMin, float mapMax)
{
    return (value - valueMin) / (valueMax - valueMin) * (mapMax - mapMin) + mapMin;
}

vec3 MapRangeToAnOther(vec3 value, vec3 valueMin, vec3 valueMax, vec3 mapMin, vec3 mapMax)
{
    return (value - valueMin) / (valueMax - valueMin) * (mapMax - mapMin) + mapMin;
}

vec3 MapToZeroOne(vec3 value, vec3 rangeMin, vec3 rangeMax)
{
    return MapRangeToAnOther(value, rangeMin, rangeMax, vec3(0.0), vec3(1.0));
}

vec3 GetTriangleNormal(vec3 p0, vec3 p1, vec3 p2)
{
    vec3 p0p1 = p1 - p0;
    vec3 p0p2 = p2 - p0;
    vec3 triNormal = normalize(cross(p0p1, p0p2));
    return triNormal;
}

mat3 ConstructBasis(vec3 normal)
{
    // Source: https://www.shadertoy.com/view/tsGcWV

    float sign_ = sign(normal.z);
    float a = -1.0 / (sign_ + normal.z);
    float b = normal.x * normal.y * a;
    vec3 tangent = vec3(1.0 + sign_ * normal.x * normal.x * a, sign_ * b, -sign_ * normal.x);
    vec3 bitangent = vec3(b, sign_ + normal.y * normal.y * a, -normal.y);
    return mat3(tangent, bitangent, normal);
}

mat3 GetTBN(vec3 tangent, vec3 normal)
{
    vec3 N = normal;
    vec3 T = tangent;
    // Gramschmidt Process (makes sure T and N always 90 degress)
    T = normalize(T - dot(T, N) * N);
    vec3 B = cross(N, T);    
    return mat3(T, B, N);
}


vec3 PolarToCartesian(float azimuth, float elevation, float len)
{
    // https://en.wikipedia.org/wiki/Spherical_coordinate_system
    // azimuth   = phi
    // elevation = theta
    // len       = rho

    float sinTheta = sin(elevation);
    return vec3(sinTheta * cos(azimuth), cos(elevation), sinTheta * sin(azimuth)) * len;
}

vec3 PolarToCartesian(float azimuth, float elevation)
{
    return PolarToCartesian(azimuth, elevation, 1.0);
}
