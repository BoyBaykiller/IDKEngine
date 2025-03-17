AppInclude(include/Surface.glsl)
AppInclude(include/Constants.glsl)

// Source: 
// https://google.github.io/filament/Filament.html#materialsystem/specularbrdf
// https://cdn2.unrealengine.com/Resources/files/2013SiggraphPresentationsNotes-26915738.pdf

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

    float f0 = r0; // same as mix(r0, albedo, metallic) with metallic=0 
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
    // Force visible specular highlight for perfect mirror
    roughness = max(roughness, 0.005);

    float a = NoH * roughness;
    float k = roughness / (1.0 - NoH * NoH + a * a);
    return k * k / PI;
}

// GGX - G Term. Accounts for Geometric masking or self shadowing
float SmithGGXCorrelated(float NoV, float NoL, float roughness)
{
    roughness = max(roughness, 0.0001);
    float ggxl = NoV * sqrt((-NoL * roughness + NoL) * NoL + roughness);
    float ggxv = NoL * sqrt((-NoV * roughness + NoV) * NoV + roughness);
    return 0.5 / (ggxv + ggxl);
}

// GGX - F Term. Accounts for different reflectivity from different angles
vec3 FresnelSchlick(vec3 f0, vec3 f90, float cosTheta)
{
    return f0 + (f90 - f0) * pow(1.0 - cosTheta, 5.0);
}
float FresnelSchlick(float f0, float f90, float cosTheta)
{
    return f0 + (f90 - f0) * pow(1.0 - cosTheta, 5.0);
}

vec3 GGXBrdf(Surface surface, vec3 V, vec3 L, float prevIor, out vec3 F)
{
    // V = incomming vector but negated (so pointing to sender)
    // L = outgoing vector (explicitly to a light with direct lighting)
    // H = halfway vector between V and L

    surface.Roughness *= surface.Roughness; // just a convention to make roughness feel more linear perceptually

    vec3 f0 = BaseReflectivity(surface.Albedo, surface.Metallic, prevIor, surface.IOR);
    vec3 f90 = vec3(1.0);

    vec3 H = normalize(V + L);
    float NoV = abs(dot(surface.Normal, V));
    float NoL = clamp(dot(surface.Normal, L), 0.0, 1.0);
    float NoH = clamp(dot(surface.Normal, H), 0.0, 1.0);
    float LoH = clamp(dot(L, H), 0.0, 1.0);

    float D = DistributionGGX(NoH, surface.Roughness);
    float G = SmithGGXCorrelated(NoV, NoL, surface.Roughness);
    F = FresnelSchlick(f0, f90, LoH);

    vec3 specular = (D * G * F); // max((4.0 * NoL * NoV), 0.001)

    return specular;
}

vec3 GGXBrdf(Surface surface, vec3 V, vec3 L, float prevIor)
{
    vec3 F;
    return GGXBrdf(surface, V, L, prevIor, F);
}
