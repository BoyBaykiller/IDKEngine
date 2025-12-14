AppInclude(include/Ray.glsl)
AppInclude(include/Surface.glsl)
AppInclude(include/Sampling.glsl)
AppInclude(include/TraceCone.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

struct ConeTraceGISettings
{
    int MaxSamples;
    float StepMultiplier;
    float GIBoost;
    float GISkyBoxBoost;
    float NormalRayOffset;
    bool IsTemporalAccumulation;
};

vec2 GetPixelCoord()
{
#if APP_SHADER_STAGE_FRAGMENT
    return gl_FragCoord.xy;
#else
    return vec2(gl_GlobalInvocationID.xy);
#endif
}

float GetMaterialVariance(float specularChance, float roughness)
{
    float diffuseChance = 1.0 - specularChance;
    float variance = diffuseChance + specularChance * roughness;
    return variance;
}

vec3 IndirectLight(Surface surface, sampler3D samplerVoxels, vec3 position, vec3 incomming, ConeTraceGISettings settings)
{    
    surface.Roughness *= surface.Roughness; // convention that makes roughness appear more linear
    
    vec3 irradiance = vec3(0.0);
    float materialVariance = GetMaterialVariance(surface.Metallic, surface.Roughness);
    uint samples = uint(mix(1.0, float(settings.MaxSamples), materialVariance));

    bool taaEnabled = taaDataUBO.TemporalAntiAliasingMode != ENUM_ANTI_ALIASING_MODE_NONE;
    uint noiseIndex = (settings.IsTemporalAccumulation && taaEnabled) ? (perFrameDataUBO.Frame % taaDataUBO.SampleCount) * settings.MaxSamples : 0u;
    vec2 pixelCoord = GetPixelCoord();
    for (uint i = 0; i < samples; i++)
    {
        float rnd0 = InterleavedGradientNoise(pixelCoord, noiseIndex + 0);
        float rnd1 = InterleavedGradientNoise(pixelCoord, noiseIndex + 1);
        float rnd2 = InterleavedGradientNoise(pixelCoord, noiseIndex + 2);
        noiseIndex++;
        
        Ray coneRay;
        coneRay.Origin = position;
        coneRay.Direction;

        vec3 diffuseDir = CosineSampleHemisphere(surface.Normal, vec2(rnd0, rnd1));
        
        const float maxConeAngle = 0.32;  // 18 degree
        const float minConeAngle = 0.0; // 0.29 degree
        float coneAngle;
        if (surface.Metallic > rnd2)
        {
            vec3 reflectionDir = reflect(incomming, surface.Normal);
            reflectionDir = normalize(mix(reflectionDir, diffuseDir, surface.Roughness));
            coneRay.Direction = reflectionDir;
            
            coneAngle = mix(minConeAngle, maxConeAngle, surface.Roughness);
        }
        else
        {
            coneRay.Direction = diffuseDir;
            coneAngle = maxConeAngle;
        }

        vec4 coneTrace = TraceCone(samplerVoxels, coneRay, surface.Normal, coneAngle, settings.StepMultiplier, settings.NormalRayOffset, 0.99);
        coneTrace += (1.0 - coneTrace.a) * (texture(skyBoxUBO.Albedo, coneRay.Direction) * settings.GISkyBoxBoost);
        
        irradiance += coneTrace.rgb;
    }
    irradiance /= float(samples);

    return irradiance * settings.GIBoost;
}