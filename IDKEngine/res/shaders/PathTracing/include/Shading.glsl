AppInclude(include/Random.glsl)

#define RAY_TYPE_DIFFUSE    0
#define RAY_TYPE_SPECULAR   1
#define RAY_TYPE_REFRACTIVE 2

struct RayProperties
{
    vec3 Direction;
    float Ior;
    float RayTypeProbability;
    uint RayType;
};

RayProperties SampleMaterial(vec3 incomming, float specularChance, float roughness, float transmissionChance, float ior, float prevIor, vec3 normal, bool fromInside)
{
    RayProperties result;
    roughness *= roughness; // just a convention to make roughness more linear perceptually

    float rnd = GetRandomFloat01();
    vec3 diffuseRayDir = CosineSampleHemisphere(normal);
    if (specularChance > rnd)
    {
        vec3 reflectionRayDir = reflect(incomming, normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRayDir, roughness));
        
        result.Direction = reflectionRayDir;
        result.RayTypeProbability = specularChance;
        result.Ior = prevIor;

        result.RayType = RAY_TYPE_SPECULAR;
    }
    else if (specularChance + transmissionChance > rnd)
    {
        if (fromInside)
        {
            // we don't actually know wheter the next mesh we hit has ior 1.0
            result.Ior = 1.0;
        }
        else
        {
            result.Ior = ior;
        }

        vec3 refractionRayDir = refract(incomming, normal, prevIor / result.Ior);
        bool totalInternalReflection = refractionRayDir == vec3(0.0);
        if (totalInternalReflection)
        {
            refractionRayDir = reflect(incomming, normal);
            result.Ior = prevIor;
        }
        result.RayType = totalInternalReflection ? RAY_TYPE_SPECULAR : RAY_TYPE_REFRACTIVE;
        refractionRayDir = normalize(mix(refractionRayDir, !totalInternalReflection ? -diffuseRayDir : diffuseRayDir, roughness));
        
        result.Direction = refractionRayDir;
        result.RayTypeProbability = transmissionChance;
    }
    else
    {
        result.Direction = diffuseRayDir;
        result.RayTypeProbability = 1.0 - specularChance - transmissionChance;

        result.Ior = prevIor;
        result.RayType = RAY_TYPE_DIFFUSE;
    }
    result.RayTypeProbability = max(result.RayTypeProbability, 0.001);

    return result;
}

vec3 ApplyAbsorption(vec3 color, vec3 absorbance, float t)
{
    // Beer's law
    return color * exp(-absorbance * t);
}

float FresnelSchlick(float cosTheta, float n1, float n2)
{
    float r0 = (n1 - n2) / (n1 + n2);
    r0 *= r0;

    return r0 + (1.0 - r0) * pow(1.0 - cosTheta, 5.0);
}

float SpecularBasedOnViewAngle(float specularChance, float cosTheta, float prevIor, float newIor)
{
    if (specularChance > 0.0) // adjust specular chance based on view angle
    {
        float newSpecularChance = mix(specularChance, 1.0, FresnelSchlick(cosTheta, prevIor, newIor));
        specularChance = newSpecularChance;
    }

    return specularChance;
}
