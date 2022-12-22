#version 460 core
#define EMISSIVE_MATERIAL_MULTIPLIER 5.0
#define PI 3.14159265
#define EPSILON 0.001
#extension GL_ARB_bindless_texture : require
#extension GL_NV_shader_atomic_fp16_vector : enable
#ifdef GL_NV_shader_atomic_fp16_vector
#extension GL_NV_gpu_shader5 : require
#endif

#ifdef GL_NV_shader_atomic_fp16_vector
layout(binding = 0, rgba16f) restrict uniform image3D ImgVoxelsAlbedo;
#else
layout(binding = 0, r32ui) restrict uniform uimage3D ImgVoxelsAlbedo;
#endif

layout(binding = 1, r32ui) restrict uniform uimage3D ImgFragCounter;

struct Material
{
    sampler2D Albedo;
    sampler2D Normal;
    sampler2D Roughness;
    sampler2D Specular;
    sampler2D Emissive;
};

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    float _pad0;
};

struct PointShadow
{
    samplerCube Sampler;
    samplerCubeShadow SamplerShadow;
    
    mat4 ProjViewMatrices[6];

    float NearPlane;
    float FarPlane;
    int LightIndex;
    float _pad0;
};

layout(std430, binding = 5) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    #define GLSL_MAX_UBO_POINT_SHADOW_COUNT 16 // used in shader and client code - keep in sync!
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int PointCount;
} shadowDataUBO;

layout(std140, binding = 2) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 5) uniform VXGIDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} vxgiDataUBO;

in InOutVars
{
    centroid vec3 FragPos;
    centroid vec2 TexCoord;
    centroid vec3 Normal;
    flat uint MaterialIndex;
    flat float EmissiveBias;
} inData;

vec3 GetdirectLightingLighting(Light light, vec3 albedo, vec3 sampleToLight);
float Visibility(PointShadow pointShadow, vec3 lightToSample);
ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos);

void main()
{
    ivec3 voxelPos = WorlSpaceToVoxelImageSpace(inData.FragPos);

    Material material = materialSSBO.Materials[inData.MaterialIndex];
    vec4 albedo = texture(material.Albedo, inData.TexCoord);
    vec3 emissive = (texture(material.Emissive, inData.TexCoord).rgb * EMISSIVE_MATERIAL_MULTIPLIER + inData.EmissiveBias) * albedo.rgb;

    uint fragCounter = imageLoad(ImgFragCounter, voxelPos).x;
    float avgMultiplier = 1.0 / float(fragCounter);

    vec3 outRadiance = vec3(0.0);
    for (int i = 0; i < shadowDataUBO.PointCount; i++)
    {
        PointShadow pointShadow = shadowDataUBO.PointShadows[i];
        Light light = lightsUBO.Lights[i];
        vec3 sampleToLight = light.Position - inData.FragPos;
        outRadiance += GetdirectLightingLighting(light, albedo.rgb, sampleToLight) * Visibility(pointShadow, -sampleToLight);
    }

    for (int i = shadowDataUBO.PointCount; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];
        vec3 sampleToLight = light.Position - inData.FragPos;
        outRadiance += GetdirectLightingLighting(light, albedo.rgb, sampleToLight);
    }
    const float ambient = 0.03;
    outRadiance += albedo.rgb * ambient;
    outRadiance += emissive;

#ifdef GL_NV_shader_atomic_fp16_vector

    vec4 normalizedAlbedo = vec4(outRadiance, albedo.a) * avgMultiplier;
    imageAtomicAdd(ImgVoxelsAlbedo, voxelPos, f16vec4(normalizedAlbedo));
    // imageAtomicMax(ImgVoxelsAlbedo, voxelPos, f16vec4(normalizedAlbedo));

#else

    outRadiance = clamp(outRadiance, 0.0, 1.0); // prevent some overflow because of limited precision
    vec4 normalizedAlbedo = vec4(outRadiance, albedo.a);
    uvec4 quantizedAlbedoRgba = uvec4(normalizedAlbedo * 255.0);
    uint packedAlbedo = (quantizedAlbedoRgba.a << 24) | (quantizedAlbedoRgba.b << 16) | (quantizedAlbedoRgba.g << 8) | (quantizedAlbedoRgba.r << 0);
    // imageAtomicAdd(ImgVoxelsAlbedo, voxelPos, packedAlbedo);
    imageAtomicMax(ImgVoxelsAlbedo, voxelPos, packedAlbedo);

#endif

}

vec3 GetdirectLightingLighting(Light light, vec3 albedo, vec3 sampleToLight)
{
    float sampleToLightLength = length(sampleToLight);

    vec3 lightDir = sampleToLight / sampleToLightLength;
    float cosTerm = dot(inData.Normal, lightDir);
    if (cosTerm > 0.0)
    {
        vec3 diffuse = light.Color * cosTerm * albedo;
        vec3 attenuation = light.Color / (PI * sampleToLightLength * sampleToLightLength);

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
    
    float shadowFactor = texture(pointShadow.SamplerShadow, vec4(lightToSample, mapedDepth));
    return shadowFactor;
}

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos)
{
    vec3 ndc = (vxgiDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz;
    ivec3 voxelPos = ivec3((ndc * 0.5 + 0.5) * imageSize(ImgVoxelsAlbedo));
    return voxelPos;
}   