AppInclude(include/Constants.glsl)

struct Surface
{
    vec3 Albedo;
    vec3 Normal;
    float Metallic;
    float PerceivedRoughness;
};

float GetAttenuationFactor(float distSq, float lightRadius)
{
    lightRadius = max(lightRadius, 0.0001);
    distSq = max(distSq, 0.0001);

    float factor = (lightRadius * lightRadius) / distSq;

    return factor;
}

// GGX - D Term. Normal distribution function
float DistributionGGX(float NoH, float roughness)
{
    float a = NoH * roughness;

    float numer = max(roughness, 0.001);
    float denom = max((1.0 - NoH * NoH + a * a), 0.0001);

    float k = numer / denom;
    return k * k * (1.0 / PI);
}

// GGX - F Term. Accounts for different reflectivity from different angles
vec3 FresnelSchlick(float u, vec3 f0, vec3 f90)
{
    return f0 + (vec3(f90) - f0) * pow(1.0 - u, 5.0);
}

// GGX - G Term. Accounts for Geometric masking or self shadowing
float SmithGGXCorrelated(float NoV, float NoL, float roughness)
{
    roughness = max(roughness, 0.0001);
    float ggxl = NoV * sqrt((-NoL * roughness + NoL) * NoL + roughness);
    float ggxv = NoL * sqrt((-NoV * roughness + NoV) * NoV + roughness);
    return 0.5 / (ggxv + ggxl);
}

vec3 BRDF(Surface surface, vec3 V, vec3 L, float ambientOcclusion)
{
    // V = surface to camera
    // L = surface to light

    vec3 f0 = mix(vec3(0.03), surface.Albedo, surface.Metallic);
    vec3 f90 = vec3(0.85);

    vec3 H = normalize(V + L);

    float NoV = abs(dot(surface.Normal, V));
    float NoL = clamp(dot(surface.Normal, L), 0.0, 1.0);
    float NoH = clamp(dot(surface.Normal, H), 0.0, 1.0);
    float LoH = clamp(dot(L, H), 0.0, 1.0);

    float roughness = surface.PerceivedRoughness * surface.PerceivedRoughness;

    float D = DistributionGGX(NoH, roughness);
    float G = SmithGGXCorrelated(NoV, NoL, roughness);
    vec3  F = FresnelSchlick(LoH, f0, f90);

    vec3 specular = (F * G * D) / max((4.0 * NoV), 0.01);
    vec3 diffuse = surface.Albedo * ambientOcclusion;

    return specular + diffuse * (vec3(1.0) - F) * (1.0 - surface.Metallic);
}