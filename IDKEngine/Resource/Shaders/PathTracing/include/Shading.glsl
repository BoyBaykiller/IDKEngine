AppInclude(include/Pbr.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Math.glsl)

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

ENUM_BSDF SelectBsdf(Surface surface)
{
    float specularChance = surface.Metallic;
    float transmissionChance = surface.Transmission;
    float diffuseChance = 1.0 - specularChance - transmissionChance;

    float rnd = GetRandomFloat01();
    if (specularChance > rnd)
    {
        return ENUM_BSDF_SPECULAR;
    }
    else if (specularChance + transmissionChance > rnd)
    {
        return ENUM_BSDF_TRANSMISSIVE;
    }
    else
    {
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
        surface.Transmission = max(1.0 - diffuseChance - surface.Metallic, 0.0); // renormalize
    }

    SampleMaterialResult result;

    result.BsdfType = SelectBsdf(surface);

    // vec3 diffuseRayDir = CosineSampleHemisphere(surface.Normal);

    // Slightly lower noise, but increases register usage (from 72 to 80)
    // https://discord.com/channels/318590007881236480/377557956775903232/1446938138722308309    
    uint saved = GetCurrentRandomSeed();
    InitializeRandomSeed((gl_GlobalInvocationID.y * 4096 + gl_GlobalInvocationID.x));
    vec2 r2 = R2Sequence(wavefrontPTSSBO.AccumulatedSamples);
    vec2 pixelOffset = vec2(GetRandomFloat01(), GetRandomFloat01());
    vec2 uv = DecorrelateSequence(r2, pixelOffset);
    vec3 diffuseRayDir = CosineSampleHemisphere(surface.Normal, uv);
    InitializeRandomSeed(saved);

    if (result.BsdfType == ENUM_BSDF_DIFFUSE)
    {
        result.RayDirection = diffuseRayDir;
        result.NewIor = prevIor;

        // BSDF=cosTheta*albedo/PI / PDF=cosine/PI =>
        // BSDF=albedo             / PDF=1
        result.Bsdf = surface.Albedo;
        result.Pdf = 1.0;
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
        if (!surface.IsVolumetric)
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

        bool gltfWantsTint = surface.IsVolumetric || !fromInside;
        if (gltfWantsTint && surface.TintOnTransmissive)
        {
            result.Bsdf = surface.Albedo;
        }
        else
        {
            result.Bsdf = vec3(1.0);
        }
        result.Pdf = 1.0;
    }
    result.Pdf = max(result.Pdf, 0.0001);

    return result;
}
