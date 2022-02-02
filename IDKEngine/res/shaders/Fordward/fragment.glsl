#version 460 core
#define PI 3.14159265
#define EPSILON 0.0001
#extension GL_ARB_bindless_texture : require
layout(early_fragment_tests) in;

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 NormalColor;
layout(location = 2) out int MeshIndexColor;

struct Material
{
    sampler2D Albedo;
    uvec2 _pad0;

    sampler2D Normal;
    uvec2 _pad1;

    sampler2D Metallic;
    uvec2 _pad2;

    sampler2D Roughness;
    uvec2 _pad3;

    sampler2D Specular;
    uvec2 _pad4;
};

struct Mesh
{
    mat4 Model[1];
    int MaterialIndex;
    int BVHEntry;
    int _pad0;
    int _pad1;
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
    samplerCubeShadow Sampler;
    float NearPlane;
    float FarPlane;

    mat4 ProjViewMatrices[6];

    vec3 _pad0;
    int LightIndex;
};

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    mat4 Projection;
    mat4 InvProjection;
} basicDataUBO;

layout(std140, binding = 1) uniform MaterialUBO
{
    Material Materials[384];
} materialUBO;

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[8];
    int PointCount;
} shadowDataUBO;

layout(std140, binding = 3) uniform LightsUBO
{
    Light Lights[128];
    int LightCount;
} lightsUBO;


in InOutVars
{
    vec2 TexCoord;
    vec3 FragPos;
    mat3 TBN;
    flat int MeshIndex;
    flat int MaterialIndex;
} inData;

vec3 PBR(Light light);
float Shadow(PointShadow pointShadow);
float DistributionGGX(float nDotH, float roughness);
float GeometrySchlickGGX(float nDotV, float roughness);
float GeometrySmith(float nDotV, float nDotL, float roughness);
vec3 FresnelSchlick(float cosTheta, vec3 F0);

vec4 Albedo;
vec3 Normal;
float Metallic;
float Roughness;
// TODO: Incorporate Specular into pbr
float Specular;

vec3 ViewDir;
vec3 F0;
void main()
{
    Material material = materialUBO.Materials[inData.MaterialIndex];
    
    // Albedo     = textureLod(material.Albedo, inData.TexCoord, textureQueryLod(material.Albedo, inData.TexCoord).y - 0.5);
    Albedo     = texture(material.Albedo, inData.TexCoord);
    Normal     = texture(material.Normal, inData.TexCoord).rgb;
    Metallic   = texture(material.Metallic, inData.TexCoord).r;
    Roughness  = texture(material.Roughness, inData.TexCoord).r;
    Specular  = texture(material.Specular, inData.TexCoord).r;

    Normal = inData.TBN * (Normal * 2.0 - 1.0);
    ViewDir = normalize(basicDataUBO.ViewPos - inData.FragPos);
    F0 = mix(vec3(0.04), Albedo.rgb, Metallic);

    vec3 irradiance = vec3(0.0);
    for (int i = 0; i < shadowDataUBO.PointCount; i++)
    {
        PointShadow pointShadow = shadowDataUBO.PointShadows[i];
        irradiance += PBR(lightsUBO.Lights[pointShadow.LightIndex]) * Shadow(pointShadow);
    }

    for (int i = shadowDataUBO.PointCount; i < lightsUBO.LightCount; i++)
    {
        irradiance += PBR(lightsUBO.Lights[i]);
    }
    irradiance /= lightsUBO.LightCount;

    FragColor = vec4(irradiance + Albedo.rgb * 0.05, 1.0);
    NormalColor = vec4(Normal, Specular);
    MeshIndexColor = inData.MeshIndex;
}

vec3 PBR(Light light)
{
    vec3 fragToLight = light.Position - inData.FragPos;
    vec3 radianceIn = light.Color / dot(fragToLight, fragToLight);
    
    vec3 lightDir = normalize(fragToLight);
    vec3 halfway = normalize(lightDir + ViewDir);
    
    float nDotV = max(dot(Normal, ViewDir), 0.0);
    float nDotL = max(dot(Normal, lightDir), 0.0);
    float nDotH = max(dot(Normal, halfway), 0.0);

    float NDF = DistributionGGX(nDotH, Roughness);
    float G = GeometrySmith(nDotV, nDotL, Roughness);
    vec3 F = FresnelSchlick(max(dot(halfway, ViewDir), 0.0), F0);

    vec3 kD = vec3(1.0) - F;
    kD *= 1.0 - Metallic;

    vec3 numerator = NDF * G * F;
    float denominator = 4.0 * nDotV * nDotL;
    vec3 calcSpec = numerator / max(denominator, EPSILON);

    return (kD * (Albedo.rgb / PI) + calcSpec) * radianceIn * nDotL;
}

// From: https://learnopengl.com/Advanced-Lighting/Shadows/Point-Shadows
const vec3 sampleOffsetDirections[20] =
{
   vec3( 1.0,  1.0,  1.0 ), vec3(  1.0, -1.0,  1.0 ), vec3( -1.0, -1.0,  1.0 ), vec3( -1.0,  1.0,  1.0 ), 
   vec3( 1.0,  1.0, -1.0 ), vec3(  1.0, -1.0, -1.0 ), vec3( -1.0, -1.0, -1.0 ), vec3( -1.0,  1.0, -1.0 ),
   vec3( 1.0,  1.0,  0.0 ), vec3(  1.0, -1.0,  0.0 ), vec3( -1.0, -1.0,  0.0 ), vec3( -1.0,  1.0,  0.0 ),
   vec3( 1.0,  0.0,  1.0 ), vec3( -1.0,  0.0,  1.0 ), vec3(  1.0,  0.0, -1.0 ), vec3( -1.0,  0.0, -1.0 ),
   vec3( 0.0,  1.0,  1.0 ), vec3(  0.0, -1.0,  1.0 ), vec3(  0.0, -1.0, -1.0 ), vec3(  0.0,  1.0, -1.0 )
};  

float Shadow(PointShadow pointShadow)
{
    vec3 lightToFrag = vec3(inData.FragPos - lightsUBO.Lights[pointShadow.LightIndex].Position);

    float twoDist = dot(lightToFrag, lightToFrag);
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;
    float twoBias = 0.0;
    const float MIN_BIAS = 0.001;
    const float MAX_BIAS = 0.05;
    twoBias = mix(MAX_BIAS * MAX_BIAS, MIN_BIAS * MIN_BIAS, max(dot(Normal, normalize(lightToFrag)), 0.0));

    // Map from [nearPlane; farPlane] to [0.0; 1.0]
    float mapedDepth = (twoDist - twoBias - twoNearPlane) / (twoFarPlane - twoNearPlane);
    
    const float DISK_RADIUS = 0.04;
    float shadowFactor = 0.0;
    for (int i = 0; i < 20; ++i)
    {
        shadowFactor += texture(pointShadow.Sampler, vec4(lightToFrag + sampleOffsetDirections[i] * DISK_RADIUS, mapedDepth));
    }

    return shadowFactor / 20.0;
}

float DistributionGGX(float nDotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float ndotH2 = nDotH * nDotH;

    float nom = a2;
    float denom = (ndotH2 * (a2 - 1.0) + 1.0);
    denom = PI * denom * denom;

    return nom / denom;
}

float GeometrySchlickGGX(float nDotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;

    float nom = nDotV;
    float denom = nDotV * (1.0 - k) + k;

    return nom / denom;
}

float GeometrySmith(float nDotV, float nDotL, float roughness)
{
    return GeometrySchlickGGX(nDotV, roughness) * GeometrySchlickGGX(nDotL, roughness);
}

vec3 FresnelSchlick(float cosTheta, vec3 F0)
{
    float val = 1.0 - cosTheta;
    return F0 + (1.0 - F0) * val * val * val * val * val;
}
