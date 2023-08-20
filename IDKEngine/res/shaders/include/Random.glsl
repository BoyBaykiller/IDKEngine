#ifndef Random_H
#define Random_H

uint Random_RNGSeed;

void InitializeRandomSeed(uint value)
{
    Random_RNGSeed = value;
}

// Faster and much more random than Wang Hash
// Source: https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/
uint GetPCGHash(inout uint seed)
{
    seed = seed * 747796405u + 2891336453u;
    uint word = ((seed >> ((seed >> 28u) + 4u)) ^ seed) * 277803737u;
    return (word >> 22u) ^ word;
}

float GetRandomFloat01()
{
    return float(GetPCGHash(Random_RNGSeed)) / 4294967296.0;
}

// Source: https://www.shadertoy.com/view/WsfBDf
float InterleavedGradientNoise(vec2 imgCoord, uint index)
{
    imgCoord += float(index) * 5.588238;
    return fract(52.9829189 * fract(0.06711056 * imgCoord.x + 0.00583715 * imgCoord.y));
}

// -------------------------------------------- //

vec3 UniformSampleSphere()
{
    float z = GetRandomFloat01() * 2.0 - 1.0;
    float a = GetRandomFloat01() * 2.0 * PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);

    return vec3(x, y, z);
}

vec3 UniformSampleSphere(float rnd0, float rnd1)
{
    float z = rnd0 * 2.0 - 1.0;
    float a = rnd1 * 2.0 * PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);

    return vec3(x, y, z);
}

// Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
vec3 CosineSampleHemisphere(vec3 normal)
{
    // Convert unit vector in sphere to a cosine weighted vector in hemisphere
    return normalize(normal + UniformSampleSphere());
}

vec3 CosineSampleHemisphere(vec3 normal, float rnd0, float rnd1)
{
    // Convert unit vector in sphere to a cosine weighted vector in hemisphere
    return normalize(normal + UniformSampleSphere(rnd0, rnd1));
}

vec2 UniformSampleDisk()
{
    vec2 point;
    float dist;
    do
    {
        point = vec2(GetRandomFloat01(), GetRandomFloat01()) * 2.0 - 1.0;
        dist = dot(point, point);
    } while (dist > 1.0);

    return point;
}
#endif