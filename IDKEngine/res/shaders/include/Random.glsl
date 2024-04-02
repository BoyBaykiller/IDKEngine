AppInclude(include/Constants.glsl)

uint Random_RNGSeed;

void InitializeRandomSeed(uint value)
{
    Random_RNGSeed = value;
}

uint GetPCGHash(inout uint seed)
{
    // Faster and much more random than Wang Hash
    // Source: https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/

    seed = seed * 747796405u + 2891336453u;
    uint word = ((seed >> ((seed >> 28u) + 4u)) ^ seed) * 277803737u;
    return (word >> 22u) ^ word;
}

float GetRandomFloat01()
{
    return float(GetPCGHash(Random_RNGSeed)) / 4294967296.0;
}

float InterleavedGradientNoise(vec2 imgCoord, uint index)
{
    // Source: https://www.shadertoy.com/view/WsfBDf
    
    imgCoord += float(index) * 5.588238;
    return fract(52.9829189 * fract(0.06711056 * imgCoord.x + 0.00583715 * imgCoord.y));
}

vec3 SampleCone(vec3 normal, float phi, float sinTheta, float cosTheta)
{
    // Source: https://www.shadertoy.com/view/4sSXWt
    
    vec3 w = normal;
    vec3 u = normalize(cross(w.yzx, w));
    vec3 v = cross(w, u);
    return (u * cos(phi) + v * sin(phi)) * sinTheta + w * cosTheta;
}

vec3 SampleLight(vec3 toSphere, float sphereRadius, float rnd0, float rnd1, out float distanceToSphere, out float solidAnglePdf)
{
    // Source: https://www.pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Light_Sources#x2-SamplingSpheres

    float radiusSq = sphereRadius * sphereRadius;
    float distanceSq = dot(toSphere, toSphere);
    float sinThetaMaxSq = radiusSq / distanceSq;
    float cosThetaMax = sqrt(max(0.0, 1.0 - sinThetaMaxSq));

    vec3 dir = normalize(toSphere);
    float cosTheta = mix(cosThetaMax, 1.0, max(rnd0, 0.001));
    float sinTheta = sqrt(max(0.0, 1.0 - cosTheta * cosTheta));
    float phi = 2.0 * PI * rnd1;
    
    solidAnglePdf = 1.0 / (2.0 * PI * (1.0 - cosThetaMax));
    distanceToSphere = length(toSphere) * cosTheta - sqrt(radiusSq - distanceSq * sinTheta * sinTheta);
    vec3 dirInCone = SampleCone(dir, phi, sinTheta, cosTheta);

    return dirInCone;
}

vec3 SampleLight(vec3 toSphere, float sphereRadius, out float distanceToSphere, out float solidAnglePdf)
{
    return SampleLight(toSphere, sphereRadius, GetRandomFloat01(), GetRandomFloat01(), distanceToSphere, solidAnglePdf);
}

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

vec3 UniformSampleHemisphere(vec3 normal, float rnd0, float rnd1)
{
    vec3 dir = UniformSampleSphere(rnd0, rnd1);
    return dir * sign(dot(dir, normal));
}

vec3 UniformSampleHemisphere(vec3 normal)
{
    return UniformSampleHemisphere(normal, GetRandomFloat01(), GetRandomFloat01());
}

vec3 CosineSampleHemisphere(vec3 normal)
{
    // Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
    
    return normalize(normal + UniformSampleSphere());
}

vec3 CosineSampleHemisphere(vec3 normal, float rnd0, float rnd1)
{
    return normalize(normal + UniformSampleSphere(rnd0, rnd1));
}

vec2 UniformSampleDisk(float rnd0, float rnd1)
{
    vec2 point;
    float dist;
    do
    {
        point = vec2(rnd0, rnd1) * 2.0 - 1.0;
        dist = dot(point, point);
    } while (dist > 1.0);

    return point;
}

vec2 UniformSampleDisk()
{
    return UniformSampleDisk(GetRandomFloat01(), GetRandomFloat01());
}

vec3 UniformSampleDisk(vec3 normal, float rnd0, float rnd1)
{
    // Source: https://github.com/LWJGL/lwjgl3-demos/blob/main/res/org/lwjgl/demo/opengl/raytracing/randomCommon.glsl#L14
    
    vec2 diskSample = UniformSampleDisk(rnd0, rnd1);

    vec3 tangent = vec3(1.0, 0.0, 0.0);
    vec3 bitangent = cross(tangent, normal);
    tangent = cross(bitangent, normal);

    return tangent * diskSample.x + bitangent * diskSample.y;
}
