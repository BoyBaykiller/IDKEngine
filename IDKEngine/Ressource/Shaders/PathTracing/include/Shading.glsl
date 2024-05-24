AppInclude(include/Pbr.glsl)
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

RayProperties SampleMaterial(vec3 incomming, Surface surface, float prevIor, bool fromInside)
{
    RayProperties result;
    surface.Roughness *= surface.Roughness; // just a convention to make roughness more linear perceptually

    float rnd = GetRandomFloat01();
    vec3 diffuseRayDir = CosineSampleHemisphere(surface.Normal);
    if (surface.Metallic > rnd)
    {
        vec3 reflectionRayDir = reflect(incomming, surface.Normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRayDir, surface.Roughness));
        
        result.Direction = reflectionRayDir;
        result.RayTypeProbability = surface.Metallic;
        result.Ior = prevIor;

        result.RayType = RAY_TYPE_SPECULAR;
    }
    else if (surface.Metallic + surface.Transmission > rnd)
    {
        if (fromInside)
        {
            // we don't actually know wheter the next mesh we hit has ior 1.0
            result.Ior = 1.0;
        }
        else
        {
            result.Ior = surface.IOR;
        }

        vec3 refractionRayDir = refract(incomming, surface.Normal, prevIor / result.Ior);
        bool totalInternalReflection = refractionRayDir == vec3(0.0);
        if (totalInternalReflection)
        {
            refractionRayDir = reflect(incomming, surface.Normal);
            result.Ior = prevIor;
        }
        result.RayType = totalInternalReflection ? RAY_TYPE_SPECULAR : RAY_TYPE_REFRACTIVE;
        refractionRayDir = normalize(mix(refractionRayDir, !totalInternalReflection ? -diffuseRayDir : diffuseRayDir, surface.Roughness));
        
        result.Direction = refractionRayDir;
        result.RayTypeProbability = surface.Transmission;
    }
    else
    {
        result.Direction = diffuseRayDir;
        result.RayTypeProbability = 1.0 - surface.Metallic - surface.Transmission;

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

float SpecularBasedOnViewAngle(float specularChance, float cosTheta, float prevIor, float newIor)
{
    if (specularChance > 0.0) // adjust specular chance based on view angle
    {
        float f0 = BaseReflectivity(prevIor, newIor);
        float f90 = 1.0;
        
        float newSpecularChance = mix(specularChance, f90, FresnelSchlick(cosTheta, f0, f90));
        specularChance = newSpecularChance;
    }

    return specularChance;
}
