#define PI 3.14159265
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX
#define FLOAT_NAN (0.0 / 0.0)

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 ndc)
{   
    vec4 rayView;
    rayView.xy = mat2(inverseProj) * ndc;
    rayView.z = -1.0;
    rayView.w = 0.0;

    vec3 rayWorld = normalize((inverseView * rayView).xyz);
    return rayWorld;
}

vec3 GetWorldSpaceDirection(vec2 ndc, int face)
{
    switch (face)
    {
        case 0:
            return normalize(vec3(-1.0, -ndc.y, ndc.x));

        case 1:
            return normalize(vec3(1.0, -ndc.y, -ndc.x));
        
        case 2:
		    return normalize(vec3(-ndc.x, 1.0, -ndc.y));

        case 3:
		    return normalize(vec3(-ndc.x, -1.0, ndc.y));

        case 4:
		    return normalize(vec3(-ndc, -1.0));

        case 5:
            return normalize(vec3(ndc.x, -ndc.y, 1.0));

        default:
            return vec3(0.0);
    }
}

vec3 Interpolate(vec3 p0, vec3 p1, vec3 p2, vec3 bary)
{
    return p0 * bary.x + p1 * bary.y + p2 * bary.z;
}

vec2 Interpolate(vec2 p0, vec2 p1, vec2 p2, vec3 bary)
{
    return p0 * bary.x + p1 * bary.y + p2 * bary.z;
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

float Remap(float value, float valueMin, float valueMax, float mapMin, float mapMax)
{
    return (value - valueMin) / (valueMax - valueMin) * (mapMax - mapMin) + mapMin;
}

vec3 Remap(vec3 value, vec3 valueMin, vec3 valueMax, vec3 mapMin, vec3 mapMax)
{
    return (value - valueMin) / (valueMax - valueMin) * (mapMax - mapMin) + mapMin;
}

vec3 MapToZeroOne(vec3 value, vec3 rangeMin, vec3 rangeMax)
{
    return Remap(value, rangeMin, rangeMax, vec3(0.0), vec3(1.0));
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
    // Source: "Building an Orthonormal Basis, Revisited"
    // float sign_ = sign(normal.z);
    // float a = -1.0 / (sign_ + normal.z);
    // float b = normal.x * normal.y * a;
    // vec3 tangent = vec3(1.0 + sign_ * normal.x * normal.x * a, sign_ * b, -sign_ * normal.x);
    // vec3 bitangent = vec3(b, sign_ + normal.y * normal.y * a, -normal.y);
    // return mat3(tangent, normal, bitangent);
    
    // +X right +Y up -Z forward
    vec3 up = abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(up, normal));
    vec3 bitangent = cross(normal, tangent);
    return mat3(tangent, normal, bitangent);
}

mat3 GetTBN(vec3 tangent, vec3 normal)
{
    vec3 N = normalize(normal);
    vec3 T = normalize(tangent);
    // Gramschmidt Process (makes sure T and N always 90 degress)
    // T = normalize(T - dot(T, N) * N);
    vec3 B = normalize(cross(N, T));    
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

uint NextPowerOfTwo(uint num)
{
    return 2u << findMSB(max(1u, num) - 1u);
} 

uint LeadingZeroCount(uint x)
{
    if (x == 0)
    {
        return 32;
    }
    return 31 - findMSB(x);
}

uint DivUp(uint numerator, uint divisor)
{
    return (numerator + divisor - 1) / divisor;
}

int CeilLog2Int(uint x)
{
    if (x <= 1)
    {
        return 0;
    }

    return 32 - int(LeadingZeroCount(x - 1));
}

uint FloatToKey(float value)
{
    // Integer comparisons between numbers returned from this function behave
    // as if the original float values where compared.
    // Simple reinterpretation works only for [0, ...], but this also handles negatives

    // 1. Always flip the sign bit.
    // 2. If the sign bit was set, flip the other bits too.
    // Note: We do right shift on an int, meaning arithmetic shift

    uint f = floatBitsToUint(value);
    uint mask = (int(f) >> 31 | (1u << 31));

    return f ^ mask;
}

uvec3 FloatToKey(vec3 vec)
{
    return uvec3(FloatToKey(vec.x), FloatToKey(vec.y), FloatToKey(vec.z));
}

float KeyToFloat(uint key)
{
    uint mask = ((key >> 31) - 1) | 0x80000000;
    return uintBitsToFloat(key ^ mask);
}

vec3 KeyToFloat(uvec3 keys)
{
    return vec3(KeyToFloat(keys.x), KeyToFloat(keys.y), KeyToFloat(keys.z));
}

/// Source: https://developer.nvidia.com/blog/thinking-parallel-part-iii-tree-construction-gpu/

uint ExpandBits(uint v)
{
    v = (v * 0x00010001u) & 0xFF0000FFu;
    v = (v * 0x00000101u) & 0x0F00F00Fu;
    v = (v * 0x00000011u) & 0xC30C30C3u;
    v = (v * 0x00000005u) & 0x49249249u;

    return v;
}

// Calculates a 30-bit Morton code for the
// given 3D point located within the unit cube [0,1].
uint GetMorton(vec3 normalizedV)
{
    uint x = clamp(uint(normalizedV.x * 1024.0), 0u, 1023u);
    uint y = clamp(uint(normalizedV.y * 1024.0), 0u, 1023u);
    uint z = clamp(uint(normalizedV.z * 1024.0), 0u, 1023u);

    uint xx = ExpandBits(x);
    uint yy = ExpandBits(y);
    uint zz = ExpandBits(z);
    uint result = xx * 4 + yy * 2 + zz;

    return result;
}
