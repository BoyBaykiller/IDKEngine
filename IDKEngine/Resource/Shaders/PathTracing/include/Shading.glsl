AppInclude(include/Pbr.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Math.glsl)
AppInclude(PathTracing/include/Bsdf.glsl)

#define ENUM_BSDF uint
#define ENUM_BSDF_DIFFUSE      0u
#define ENUM_BSDF_SPECULAR     1u
#define ENUM_BSDF_TRANSMISSIVE 2u

struct SampleMaterialResult
{
    vec3 RayDirection;
    ENUM_BSDF BsdfType;

    vec3 Bsdf;
    float Pdf;

    float NewIor;
};

float SpecularBasedOnViewAngle(float specularChance, float cosTheta, float prevIor, float newIor)
{
    float f0 = BaseReflectivity(prevIor, newIor);
    float f90 = 1.0;
    
    float newSpecularChance = mix(specularChance, f90, FresnelSchlick(f0, f90, cosTheta));

    return newSpecularChance;
}

ENUM_BSDF SelectBsdf(Surface surface, out float bsdfSelectionPdf)
{
    float specularChance = surface.Metallic;
    float transmissionChance = surface.Transmission;
    float diffuseChance = 1.0 - specularChance - transmissionChance;

    float rnd = GetRandomFloat01();
    if (specularChance > rnd)
    {
        bsdfSelectionPdf = specularChance;
        return ENUM_BSDF_SPECULAR;
    }
    else if (specularChance + transmissionChance > rnd)
    {
        bsdfSelectionPdf = transmissionChance;
        return ENUM_BSDF_TRANSMISSIVE;
    }
    else
    {
        bsdfSelectionPdf = diffuseChance;
        return ENUM_BSDF_DIFFUSE;
    }
}

SampleMaterialResult SampleMaterial(vec3 incomming, Surface surface, float prevIor, bool fromInside)
{
    surface.Roughness *= surface.Roughness; // convention that makes roughness appear more linear

    float cosTheta = dot(-incomming, surface.Normal);

    {
        // Fresnel
        float diffuseChance = 1.0 - surface.Metallic - surface.Transmission;
        surface.Metallic = SpecularBasedOnViewAngle(surface.Metallic, cosTheta, prevIor, surface.IOR);
        surface.Transmission = 1.0 - diffuseChance - surface.Metallic; // renormalize
    }

    SampleMaterialResult result;

    float bsdfSelectionPdf;
    result.BsdfType = SelectBsdf(surface, bsdfSelectionPdf);

    float lambertianPdf;
    vec3 diffuseRayDir = SampleLambertian(surface.Normal, cosTheta, lambertianPdf);

    if (result.BsdfType == ENUM_BSDF_DIFFUSE)
    {
        result.RayDirection = diffuseRayDir;
        result.NewIor = prevIor;

        result.Bsdf = LambertianBrdf(surface.Albedo) * cosTheta;
        result.Pdf = lambertianPdf;
    }
    else if (result.BsdfType == ENUM_BSDF_SPECULAR)
    {
        vec3 reflectionRayDir = reflect(incomming, surface.Normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRayDir, surface.Roughness));
        result.RayDirection = reflectionRayDir;

        result.Bsdf = surface.Albedo;
        result.Pdf = 1.0;

        result.NewIor = prevIor;
    }
    else if (result.BsdfType == ENUM_BSDF_TRANSMISSIVE)
    {
        if (fromInside)
        {
            // We don't actually know wheter the next mesh we hit has ior 1.0
            result.NewIor = 1.0;
        }
        else
        {
            result.NewIor = surface.IOR;
        }

        vec3 refractionRayDir;
        bool totalInternalReflection;
        if (surface.IsThinWalled)
        {
            refractionRayDir = incomming;
            totalInternalReflection = false;
            result.NewIor = 1.0;
        }
        else
        {
            refractionRayDir = refract(incomming, surface.Normal, prevIor / result.NewIor);
            totalInternalReflection = refractionRayDir == vec3(0.0);
            if (totalInternalReflection)
            {
                refractionRayDir = reflect(incomming, surface.Normal);
                result.NewIor = prevIor;
            }
        }
        
        refractionRayDir = normalize(mix(refractionRayDir, !totalInternalReflection ? -diffuseRayDir : diffuseRayDir, surface.Roughness));
        result.RayDirection = refractionRayDir;

        if (surface.TintOnTransmissive)
        {
            result.Bsdf = surface.Albedo;
        }
        else
        {
            result.Bsdf = vec3(1.0);
        }
        result.Pdf = 1.0;
    }
    result.Pdf = max(result.Pdf * bsdfSelectionPdf, 0.0001);

    return result;
}
