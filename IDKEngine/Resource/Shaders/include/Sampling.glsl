AppInclude(include/Random.glsl)
AppInclude(include/Math.glsl)

vec3 SampleCone(vec3 normal, float phi, float sinTheta, float cosTheta)
{
    vec3 localSample;
    localSample.x = cos(phi) * sinTheta;
    localSample.z = sin(phi) * sinTheta;
    localSample.y = cosTheta;

    mat3 basis = ConstructBasis(normal);

    return basis * localSample;
}

vec3 SampleSphere(vec3 toSphere, float sphereRadius, float rnd0, float rnd1, out float distanceToSphere, out float solidAnglePdf)
{
    // Source: https://www.pbr-book.org/3ed-2018/Light_Transport_I_Surface_Reflection/Sampling_Light_Sources#x2-SamplingSpheres

    float radiusSq = sphereRadius * sphereRadius;
    float distanceSq = dot(toSphere, toSphere);
    float sinThetaMaxSq = radiusSq / distanceSq;
    float cosThetaMax = sqrt(max(1.0 - sinThetaMaxSq, 0.0));
    float phiMax = 2.0 * PI;

    float phi = phiMax * rnd0;
    float cosTheta = mix(cosThetaMax, 1.0, max(rnd1, 0.001));
    float sinTheta = sqrt(max(1.0 - cosTheta * cosTheta, 0.0));
    
    solidAnglePdf = 1.0 / (phiMax * (1.0 - cosThetaMax));
    distanceToSphere = length(toSphere) * cosTheta - sqrt(radiusSq - distanceSq * sinTheta * sinTheta);
    vec3 dirInCone = SampleCone(normalize(toSphere), phi, sinTheta, cosTheta);

    return dirInCone;
}

vec3 SampleSphere(vec3 toSphere, float sphereRadius, out float distanceToSphere, out float solidAnglePdf)
{
    return SampleSphere(toSphere, sphereRadius, GetRandomFloat01(), GetRandomFloat01(), distanceToSphere, solidAnglePdf);
}

vec3 SampleSphere(float rnd0, float rnd1)
{
    float z = rnd0 * 2.0 - 1.0;
    float a = rnd1 * 2.0 * PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);

    return vec3(x, y, z);
}

vec3 SampleSphere()
{
    return SampleSphere(GetRandomFloat01(), GetRandomFloat01());
}

vec3 SampleHemisphere(vec3 normal, float rnd0, float rnd1)
{
    vec3 dir = SampleSphere(rnd0, rnd1);
    return dir * sign(dot(dir, normal));
}

vec3 SampleHemisphere(vec3 normal)
{
    return SampleHemisphere(normal, GetRandomFloat01(), GetRandomFloat01());
}


vec3 CosineSampleHemisphere(vec3 normal, float rnd0, float rnd1)
{
    return normalize(normal + SampleSphere(rnd0, rnd1));

    // float azimuth = 2.0 * PI * rnd0;
    // float elevation = acos(sqrt(rnd1));

    // vec3 dir = PolarToCartesian(azimuth, elevation);
    // mat3 basis = ConstructBasis(normal);

    // return basis * dir;
}

vec3 CosineSampleHemisphere(vec3 normal)
{
    // Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
    
    return CosineSampleHemisphere(normal, GetRandomFloat01(), GetRandomFloat01());
}

vec2 SampleDisk()
{
    vec2 point;
    float dist;
    float lastRnd = GetRandomFloat01();
    do
    {
        float thisRnd = GetRandomFloat01();
        
        point = vec2(lastRnd, thisRnd);
        dist = dot(point, point);

        lastRnd = thisRnd;
    } while (dist > 1.0);

    return point * 2.0 - 1.0;
}

float CosineSampleHemispherePdf(float cosTheta)
{
    return cosTheta / PI;
}

// Probability to choose pf when given pf and pg as weights. Examples:
// * pf = 1.0; pg = 0.0; ==> 1.0, meaning choose pf 100% of the time
// * pf =   x; pg =   x; ==> 0.5, meaning choose pf  50% of the time
// * pf =   3; pg =   7; ==> 0.3, meaning choose pf  30% of the time
// * pf = 0.0; pg = 1.0; ==> 0.0, meaning choose pf   0% of the time
float BalanceHeuristic(float pf, float pg)
{
    return pf / (pf + pg);
}
