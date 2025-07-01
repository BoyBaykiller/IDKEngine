AppInclude(include/Sampling.glsl)

vec3 LambertianBrdf(vec3 albedo)
{
    return albedo / PI;
}
float LambertianPdf(float cosTheta)
{
    // cosTheta integrated over hemisphere is PI so normalize using that
    return cosTheta / PI; // We use cosine weighted sampling so pdf is not 1/(2*PI)
}
vec3 SampleLambertian(vec3 normal, float cosTheta, out float pdf)
{
    pdf = LambertianPdf(cosTheta);
    return CosineSampleHemisphere(normal);
}
