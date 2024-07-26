AppInclude(include/Sampling.glsl)

float LambertianPdf(float cosTheta)
{
    return cosTheta / PI; // We use cosine weighted sampling so pdf is not 1/(2*PI)
}
vec3 SampleLambertian(vec3 normal, float cosTheta, out float pdf)
{
    pdf = LambertianPdf(cosTheta);
    return CosineSampleHemisphere(normal);
}
vec3 LambertianBrdf(vec3 albedo)
{
    return albedo / PI;
}

/// Unused for now

float BlinnPhongPdf(vec3 normal, vec3 V, vec3 L, float roughness)
{
    vec3 H = normalize(V + L);
    float cosTheta = dot(normal, H);
    float normalizationFactor = (roughness + 1.0) / (2.0 * PI);
    return pow(cosTheta, roughness) * normalizationFactor / (4.0 * dot(V, H));
}
vec3 SampleBlinnPhong(vec3 normal, vec3 V, float roughness, out float pdf)
{
    float phi = GetRandomFloat01() * 2.0 * PI;
    float cosTheta = pow(GetRandomFloat01(), 1.0 / roughness);
    float theta = acos(cosTheta);

    mat3 tbn = ConstructBasis(normal);
    vec3 worldMicrofacetNormal = tbn * PolarToCartesian(phi, theta);
    // vec3 worldMicrofacetNormal = localToWorld( sphericalToCartesian( 1.0, phi, theta ), normal );
    vec3 reflectedDir = reflect(-V, worldMicrofacetNormal);

    pdf = BlinnPhongPdf(normal, V, reflectedDir, roughness);

    return worldMicrofacetNormal;
}
vec3 BlinnPhongBrdf(vec3 albedo, vec3 normal, vec3 V, vec3 L, float roughness)
{
    vec3 H = normalize(V + L);
    float cosTheta = clamp(dot(normal, H), 0.0, 1.0);
    return albedo * ((roughness + 2.0) / (8.0 * PI) * pow(cosTheta, roughness));
}