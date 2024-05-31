AppInclude(include/Pbr.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(PathTracing/include/Bsdf.glsl)

#define RAY_TYPE_DIFFUSE    0
#define RAY_TYPE_SPECULAR   1
#define RAY_TYPE_REFRACTIVE 2

struct SampleMaterialResult
{
    vec3 RayDirection;
    uint RayType;
    float RayTypeProbability;

    vec3 Bsdf;
    float Pdf;

    float NewIor;
};

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

SampleMaterialResult SampleMaterial(vec3 incomming, Surface surface, float prevIor, bool fromInside)
{
    surface.Roughness *= surface.Roughness; // just a convention to make roughness more perceptually

    float cosTheta = dot(-incomming, surface.Normal);

    float diffuseChance = max(1.0 - surface.Metallic - surface.Transmission, 0.0);
    surface.Metallic = SpecularBasedOnViewAngle(surface.Metallic, cosTheta, prevIor, surface.IOR);
    surface.Transmission = 1.0 - diffuseChance - surface.Metallic; // normalize again to (diff + spec + trans == 1.0)

    SampleMaterialResult result;

    float rnd = GetRandomFloat01();

    float lambertianPdf;
    vec3 diffuseRayDir = SampleLambertian(surface.Normal, cosTheta, lambertianPdf);
    if (surface.Metallic > rnd)
    {
        vec3 reflectionRayDir = reflect(incomming, surface.Normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRayDir, surface.Roughness));
        
        result.RayDirection = reflectionRayDir;
        result.RayTypeProbability = surface.Metallic;
        result.RayType = RAY_TYPE_SPECULAR;

        result.NewIor = prevIor;

        result.Bsdf = LambertianBrdf(surface.Albedo);
        result.Pdf = lambertianPdf;

        // float blinnPhongPdf;
        // reflectionRayDir = SampleBlinnPhong(surface.Normal, -incomming, surface.Roughness, blinnPhongPdf);
        // result.Bsdf = BlinnPhongBrdf(surface.Albedo, surface.Normal, -incomming, reflectionRayDir, surface.Roughness);
        // result.Pdf = blinnPhongPdf;
    }
    else if (surface.Metallic + surface.Transmission > rnd)
    {
        if (fromInside)
        {
            // we don't actually know wheter the next mesh we hit has ior 1.0
            result.NewIor = 1.0;
        }
        else
        {
            result.NewIor = surface.IOR;
        }

        vec3 refractionRayDir = refract(incomming, surface.Normal, prevIor / result.NewIor);
        bool totalInternalReflection = refractionRayDir == vec3(0.0);
        if (totalInternalReflection)
        {
            refractionRayDir = reflect(incomming, surface.Normal);
            result.NewIor = prevIor;
        }
        result.RayType = totalInternalReflection ? RAY_TYPE_SPECULAR : RAY_TYPE_REFRACTIVE;
        refractionRayDir = normalize(mix(refractionRayDir, !totalInternalReflection ? -diffuseRayDir : diffuseRayDir, surface.Roughness));
        
        result.RayDirection = refractionRayDir;
        result.RayTypeProbability = surface.Transmission;

        result.Bsdf = LambertianBrdf(surface.Albedo);
        result.Pdf = lambertianPdf;
    }
    else
    {
        result.RayDirection = diffuseRayDir;
        result.RayTypeProbability = 1.0 - surface.Metallic - surface.Transmission;
        result.RayType = RAY_TYPE_DIFFUSE;

        result.NewIor = prevIor;

        result.Bsdf = LambertianBrdf(surface.Albedo);
        result.Pdf = lambertianPdf;
    }
    result.RayTypeProbability = max(result.RayTypeProbability, 0.0001);
    result.Pdf = max(result.Pdf, 0.0001);

    // result.Pdf = 1.0;
    // result.Bsdf = surface.Albedo;

    return result;
}

vec3 ApplyAbsorption(vec3 color, vec3 absorbance, float t)
{
    // Beer's law
    return color * exp(-absorbance * t);
}
