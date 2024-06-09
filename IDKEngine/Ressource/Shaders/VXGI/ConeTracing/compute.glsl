#version 460 core

AppInclude(include/Surface.glsl)
AppInclude(include/Sampling.glsl)
AppInclude(include/TraceCone.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxels;

layout(std140, binding = 7) uniform SettingsUBO
{
    int MaxSamples;
    float StepMultiplier;
    float GIBoost;
    float GISkyBoxBoost;
    float NormalRayOffset;
    bool IsTemporalAccumulation;
} settingsUBO;

vec3 IndirectLight(Surface surface, vec3 position, vec3 incomming);
float GetMaterialVariance(float specularChance, float roughness);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 1.0)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }

    vec3 fragPos = PerspectiveTransformUvDepth(vec3(uv, depth), perFrameDataUBO.InvProjView);
    vec3 normal = DecodeUnitVec(texelFetch(gBufferDataUBO.Normal, imgCoord, 0).rg);
    float specular = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).r;
    float roughness = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).g;

    Surface surface = GetDefaultSurface();
    surface.Metallic = specular;
    surface.Roughness = roughness;
    surface.Normal = normal;

    vec3 viewDir = fragPos - perFrameDataUBO.ViewPos;
    vec3 indirectLight = IndirectLight(surface, fragPos, viewDir) * settingsUBO.GIBoost;

    imageStore(ImgResult, imgCoord, vec4(indirectLight, 1.0));
}

vec3 IndirectLight(Surface surface, vec3 position, vec3 incomming)
{    
    surface.Roughness *= surface.Roughness; // just a convention to make roughness feel more linear perceptually
    
    vec3 irradiance = vec3(0.0);
    float materialVariance = GetMaterialVariance(surface.Metallic, surface.Roughness);
    uint samples = uint(mix(1.0, float(settingsUBO.MaxSamples), materialVariance));

    bool taaEnabled = taaDataUBO.TemporalAntiAliasingMode != TEMPORAL_ANTI_ALIASING_MODE_NO_AA;
    uint noiseIndex = (settingsUBO.IsTemporalAccumulation && taaEnabled) ? (perFrameDataUBO.Frame % taaDataUBO.SampleCount) * settingsUBO.MaxSamples : 0u;
    for (uint i = 0; i < samples; i++)
    {
        float rnd0 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 0);
        float rnd1 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 1);
        float rnd2 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 2);
        noiseIndex++;
        
        Ray coneRay;
        coneRay.Origin = position;
        coneRay.Direction;

        vec3 diffuseDir = CosineSampleHemisphere(surface.Normal, rnd0, rnd1);
        
        const float maxConeAngle = 0.32;  // 18 degree
        const float minConeAngle = 0.005; // 0.29 degree
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

        vec4 coneTrace = TraceCone(SamplerVoxels, coneRay, surface.Normal, coneAngle, settingsUBO.StepMultiplier, settingsUBO.NormalRayOffset, 0.99);
        coneTrace += (1.0 - coneTrace.a) * (texture(skyBoxUBO.Albedo, coneRay.Direction) * settingsUBO.GISkyBoxBoost);
        
        irradiance += coneTrace.rgb;
    }
    irradiance /= float(samples);

    return irradiance;
}

float GetMaterialVariance(float specularChance, float roughness)
{
    float diffuseChance = 1.0 - specularChance;
    float perceivedFinalRoughness = 1.0 - (specularChance * (1.0 - roughness));
    return mix(perceivedFinalRoughness, 1.0, diffuseChance);
}
