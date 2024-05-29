AppInclude(include/Surface.glsl)
AppInclude(include/Constants.glsl)

float GetAttenuationFactor(float distSq, float lightRadius)
{
    lightRadius = max(lightRadius, 0.0001);
    distSq = max(distSq, 0.0001);

    float factor = (lightRadius * lightRadius) / distSq;

    return factor;
}

float BaseReflectivity(float n1, float n2)
{
    // Compute R0 term in https://en.wikipedia.org/wiki/Schlick%27s_approximation
    float r0 = (n1 - n2) / (n1 + n2);
    r0 *= r0;

    // vec3 f0 = mix(r0, albedo, metallic);
    float f0 = r0; // assumes metallic = 0.0
    return f0;
}

vec3 BaseReflectivity(vec3 albedo, float metallic, float n1, float n2)
{
    // Compute R0 term in https://en.wikipedia.org/wiki/Schlick%27s_approximation
    vec3 r0 = (vec3(n1) - vec3(n2)) / (vec3(n1) + vec3(n2));
    r0 *= r0;

    vec3 f0 = mix(r0, albedo, metallic);
    return f0;
}

// GGX - D Term. Normal distribution function
float DistributionGGX(float NoH, float roughness)
{
    float a = NoH * roughness;

    float numer = max(roughness, 0.001);
    float denom = max((1.0 - NoH * NoH + a * a), 0.0001);

    float k = numer / denom;
    return k * k / PI;
}

// GGX - F Term. Accounts for different reflectivity from different angles
vec3 FresnelSchlick(float cosTheta, vec3 f0, vec3 f90)
{
    return f0 + (f90 - f0) * pow(1.0 - cosTheta, 5.0);
}
float FresnelSchlick(float cosTheta, float f0, float f90)
{
    return f0 + (f90 - f0) * pow(1.0 - cosTheta, 5.0);
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

    const float prevIor = 1.0;
    vec3 f0 = BaseReflectivity(surface.Albedo, surface.Metallic, prevIor, surface.IOR);
    vec3 f90 = vec3(1.0);

    vec3 H = normalize(V + L);

    float NoV = abs(dot(surface.Normal, V));
    float NoL = clamp(dot(surface.Normal, L), 0.0, 1.0);
    float NoH = clamp(dot(surface.Normal, H), 0.0, 1.0);
    float LoH = clamp(dot(L, H), 0.0, 1.0);

    float roughness = surface.Roughness * surface.Roughness;

    float D = DistributionGGX(NoH, roughness);
    float G = SmithGGXCorrelated(NoV, NoL, roughness);
    vec3  F = FresnelSchlick(LoH, f0, f90);

    vec3 specular = (F * G * D); //  / max((4.0 * NoV), 0.01)
    vec3 diffuse = surface.Albedo * ambientOcclusion;

    return specular + diffuse * (vec3(1.0) - F) * (1.0 - surface.Metallic);
}