#version 460 core

AppInclude(include/Random.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/TraceCone.glsl)

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

vec3 IndirectLight(vec3 point, vec3 incomming, vec3 normal, float specularChance, float roughness);
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
    vec3 normal = normalize(texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).rgb);
    float specular = texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).a;
    float roughness = texelFetch(gBufferDataUBO.EmissiveRoughness, imgCoord, 0).a;

    vec3 viewDir = fragPos - perFrameDataUBO.ViewPos;
    vec3 indirectLight = IndirectLight(fragPos, viewDir, normal, specular, roughness) * settingsUBO.GIBoost;

    imageStore(ImgResult, imgCoord, vec4(indirectLight, 1.0));
}

vec3 IndirectLight(vec3 position, vec3 incomming, vec3 normal, float specularChance, float roughness)
{    
    roughness *= roughness; // just a convention to make roughness feel more linear perceptually
    
    vec3 irradiance = vec3(0.0);
    float materialVariance = GetMaterialVariance(specularChance, roughness);
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

        vec3 diffuseDir = CosineSampleHemisphere(normal, rnd0, rnd1);
        
        const float maxConeAngle = 0.32;  // 18 degree
        const float minConeAngle = 0.005; // 0.29 degree
        float coneAngle;
        if (specularChance > rnd2)
        {
            vec3 reflectionDir = reflect(incomming, normal);
            reflectionDir = normalize(mix(reflectionDir, diffuseDir, roughness));
            coneRay.Direction = reflectionDir;
            
            coneAngle = mix(minConeAngle, maxConeAngle, roughness);
        }
        else
        {
            coneRay.Direction = diffuseDir;
            coneAngle = maxConeAngle;
        }

        vec4 coneTrace = TraceCone(SamplerVoxels, coneRay, normal, coneAngle, settingsUBO.StepMultiplier, settingsUBO.NormalRayOffset, 0.99);
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

