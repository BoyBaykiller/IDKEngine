#version 460 core
#define PI 3.14159265
#define EPSILON 0.001
#extension GL_NV_shader_atomic_fp16_vector : enable
#if defined GL_NV_shader_atomic_fp16_vector
#extension GL_NV_gpu_shader5 : require
#endif

layout(binding = 0, rgba16f) restrict uniform image3D ImgResult;

#if !defined GL_NV_shader_atomic_fp16_vector
layout(binding = 1, r32ui) restrict uniform uimage3D ImgResultR;
layout(binding = 2, r32ui) restrict uniform uimage3D ImgResultG;
layout(binding = 3, r32ui) restrict uniform uimage3D ImgResultB;
#endif

AppInclude(shaders/include/Buffers.glsl)

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
ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos);

void main()
{
    ivec3 voxelPos = WorlSpaceToVoxelImageSpace(inData.FragPos);

    Material material = materialSSBO.Materials[inData.MaterialIndex];
    vec4 albedoAlpha = texture(material.BaseColor, inData.TexCoord) * unpackUnorm4x8(material.BaseColorFactor);
    vec3 emissive = (texture(material.Emissive, inData.TexCoord).rgb * material.EmissiveFactor) + inData.EmissiveBias * albedoAlpha.rgb;

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];

        vec3 sampleToLight = light.Position - inData.FragPos;
        vec3 contrib = GetDirectLighting(light, albedoAlpha.rgb, sampleToLight);
        if (light.PointShadowIndex >= 0)
        {
            PointShadow pointShadow = shadowDataUBO.PointShadows[light.PointShadowIndex];
            contrib *= Visibility(pointShadow, -sampleToLight);
        }

        directLighting += contrib;
    }

    const float ambient = 0.03;
    directLighting += albedoAlpha.rgb * ambient;
    directLighting += emissive;

#if defined GL_NV_shader_atomic_fp16_vector

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
    float sampleToLightLength = length(sampleToLight);

    vec3 lightDir = sampleToLight / sampleToLightLength;
    float cosTerm = dot(inData.Normal, lightDir);
    if (cosTerm > 0.0)
    {
        vec3 diffuse = light.Color * cosTerm * albedo;
        vec3 attenuation = light.Color / (4.0 * PI * sampleToLightLength * sampleToLightLength);

        return diffuse * attenuation;
    }

    return vec3(0.0);
}

float Visibility(PointShadow pointShadow, vec3 lightToSample)
{
    float lightToFragLength = length(lightToSample);

    float twoDist = lightToFragLength * lightToFragLength;
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;
    
    const float MIN_BIAS = EPSILON;
    const float MAX_BIAS = 1.5;
    float twoBias = mix(MAX_BIAS * MAX_BIAS, MIN_BIAS * MIN_BIAS, max(dot(inData.Normal, lightToSample / lightToFragLength), 0.0));

    // Map from [nearPlane, farPlane] to [0.0, 1.0]
    float mapedDepth = (twoDist - twoBias - twoNearPlane) / (twoFarPlane - twoNearPlane);
    
    float shadowFactor = texture(pointShadow.ShadowTexture, vec4(lightToSample, mapedDepth));
    return shadowFactor;
}

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos)
{
    vec3 ndc = (voxelizerDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz;
    ivec3 voxelPos = ivec3((ndc * 0.5 + 0.5) * imageSize(ImgResult));
    return voxelPos;
}
