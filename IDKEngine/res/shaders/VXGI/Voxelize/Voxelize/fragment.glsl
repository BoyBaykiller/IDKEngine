#version 460 core

#define TAKE_ATOMIC_FP16_PATH AppInsert(TAKE_ATOMIC_FP16_PATH)

#if TAKE_ATOMIC_FP16_PATH
    #extension GL_NV_shader_atomic_fp16_vector : require
    #extension GL_NV_gpu_shader5 : require
#endif

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Pbr.glsl)

layout(binding = 0, rgba16f) restrict uniform image3D ImgResult;

#if !TAKE_ATOMIC_FP16_PATH
layout(binding = 1, r32ui) restrict uniform uimage3D ImgResultR;
layout(binding = 2, r32ui) restrict uniform uimage3D ImgResultG;
layout(binding = 3, r32ui) restrict uniform uimage3D ImgResultB;
#endif

in InOutVars
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    flat uint MaterialIndex;
    flat float EmissiveBias;
} inData;

vec3 GetDirectLighting(Light light, vec3 albedo, vec3 sampleToLight);
float Visibility(PointShadow pointShadow, vec3 lightToSample);
float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpaceSamplePos);
ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos);

void main()
{
    ivec3 voxelPos = WorlSpaceToVoxelImageSpace(inData.FragPos);

    Material material = materialSSBO.Materials[inData.MaterialIndex];
    vec4 albedoAlpha = texture(material.BaseColor, inData.TexCoord) * DecompressUR8G8B8A8(material.BaseColorFactor);
    vec3 emissive = texture(material.Emissive, inData.TexCoord).rgb * material.EmissiveFactor * MATERIAL_EMISSIVE_FACTOR + inData.EmissiveBias * albedoAlpha.rgb;

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];

        vec3 sampleToLight = light.Position - inData.FragPos;
        vec3 contrib = GetDirectLighting(light, albedoAlpha.rgb, sampleToLight);
        if (light.PointShadowIndex >= 0)
        {
            PointShadow pointShadow = shadowsUBO.PointShadows[light.PointShadowIndex];
            contrib *= Visibility(pointShadow, -sampleToLight);
        }

        directLighting += contrib;
    }

    const float ambient = 0.015;
    directLighting += albedoAlpha.rgb * ambient;
    directLighting += emissive;

#if TAKE_ATOMIC_FP16_PATH

    imageAtomicMax(ImgResult, voxelPos, f16vec4(directLighting, 1.0));

#else

    imageAtomicMax(ImgResultR, voxelPos, floatBitsToUint(directLighting.r));
    imageAtomicMax(ImgResultG, voxelPos, floatBitsToUint(directLighting.g));
    imageAtomicMax(ImgResultB, voxelPos, floatBitsToUint(directLighting.b));
    imageStore(ImgResult, voxelPos, vec4(0.0, 0.0, 0.0, 1.0));

#endif

}

vec3 GetDirectLighting(Light light, vec3 albedo, vec3 sampleToLight)
{
    float dist = length(sampleToLight);

    vec3 lightDir = sampleToLight / dist;
    float cosTerm = dot(normalize(inData.Normal), lightDir);
    if (cosTerm > 0.0)
    {
        vec3 diffuse = light.Color * cosTerm * albedo;
        float attenuation = GetAttenuationFactor(dist * dist, light.Radius);

        return diffuse * attenuation;
    }

    return vec3(0.0);
}

float Visibility(PointShadow pointShadow, vec3 lightToSample)
{
    float bias = 0.02;
    const float sampleDiskRadius = 0.08;

    float depth = GetLightSpaceDepth(pointShadow, lightToSample * (1.0 - bias));
    float visibilityFactor = texture(pointShadow.PcfShadowTexture, vec4(lightToSample, depth));

    return visibilityFactor;
}

float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpaceSamplePos)
{
    float dist = max(abs(lightSpaceSamplePos.x), max(abs(lightSpaceSamplePos.y), abs(lightSpaceSamplePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos)
{
    vec3 uvw = MapToZeroOne(worldPos, voxelizerDataUBO.GridMin, voxelizerDataUBO.GridMax);
    ivec3 voxelPos = ivec3(uvw * imageSize(ImgResult));
    return voxelPos;
}
