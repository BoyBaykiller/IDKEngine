#version 460 core
#define PI 3.14159265
#define EPSILON 0.001
#extension GL_ARB_bindless_texture : require
layout(early_fragment_tests) in;

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 NormalSpecColor;
layout(location = 2) out int MeshIndexColor;
layout(location = 3) out vec2 VelocityColor;

layout(binding = 0) uniform sampler2D SamplerAO;

struct Material
{
    sampler2D Albedo;
    uvec2 _pad0;

    sampler2D Normal;
    uvec2 _pad1;

    sampler2D Roughness;
    uvec2 _pad3;

    sampler2D Specular;
    uvec2 _pad4;
};

struct Mesh
{
    mat4 Model;
    int MaterialIndex;
    int BVHEntry;
    float Emissive;
    float NormalMapStrength;
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
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

layout(std140, binding = 1) uniform MaterialUBO
{
    Material Materials[256];
} materialUBO;

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[64];
    int PointCount;
} shadowDataUBO;

layout(std140, binding = 3) uniform LightsUBO
{
    Light Lights[64];
    int Count;
} lightsUBO;

in InOutVars
{
    vec2 TexCoord;
    vec3 FragPos;
    vec4 ClipPos;
    vec4 PrevClipPos;
    vec3 Normal;
    mat3 TBN;
    flat int MeshIndex;
    flat int MaterialIndex;
    flat float Emissive;
    flat float NormalMapStrength;
} inData;

vec3 BlinnPhong(Light light);
float Visibility(PointShadow pointShadow);
float LinearizeDepth(float depth, float nearPlane, float farPlane);

vec4 Albedo;
vec3 Normal;
float Roughness;
float Specular;
vec3 ViewDir;
void main()
{
    vec2 uv = (inData.ClipPos.xy / inData.ClipPos.w) * 0.5 + 0.5;

    Material material = materialUBO.Materials[inData.MaterialIndex];
    
    Albedo = texture(material.Albedo, inData.TexCoord);
    Normal = texture(material.Normal, inData.TexCoord).rgb;
    Roughness = texture(material.Roughness, inData.TexCoord).r;
    Specular = texture(material.Specular, inData.TexCoord).r;
    float AO = texture(SamplerAO, uv).r;

    Normal = inData.TBN * normalize(Normal * 2.0 - 1.0);
    Normal = normalize(mix(normalize(inData.Normal), Normal, inData.NormalMapStrength));

    ViewDir = normalize(basicDataUBO.ViewPos - inData.FragPos);

    vec3 irradiance = vec3(0.0);
    for (int i = 0; i < shadowDataUBO.PointCount; i++)
    {
        PointShadow pointShadow = shadowDataUBO.PointShadows[i];
        irradiance += BlinnPhong(lightsUBO.Lights[i]) * Visibility(pointShadow);
    }

    for (int i = shadowDataUBO.PointCount; i < lightsUBO.Count; i++)
    {
        irradiance += BlinnPhong(lightsUBO.Lights[i]);
    }

    vec3 emissive = inData.Emissive * Albedo.rgb;
    FragColor = vec4(irradiance + emissive + Albedo.rgb * 0.03 * (1.0 - AO), 1.0);
    NormalSpecColor = vec4(Normal, Specular);
    MeshIndexColor = inData.MeshIndex;

    vec2 prevUV = (inData.PrevClipPos.xy / inData.PrevClipPos.w) * 0.5 + 0.5;
    VelocityColor = (uv - prevUV);

    gl_FragDepth = LinearizeDepth(gl_FragCoord.z, basicDataUBO.NearPlane, basicDataUBO.FarPlane);
}

vec3 BlinnPhong(Light light)
{
    vec3 fragToLight = light.Position - inData.FragPos;
    float fragToLightLength = length(fragToLight);

    vec3 lightDir = fragToLight / fragToLightLength;
    float cosTerm = dot(Normal, lightDir);
    if (cosTerm > 0.0)
    {
        vec3 diffuse = light.Color * cosTerm * Albedo.rgb;  
    
        vec3 specular = vec3(0.0);
        vec3 halfwayDir = normalize(lightDir + ViewDir);
        float temp = dot(Normal, halfwayDir);
        if (temp > 0.0)
        {
            float spec = pow(temp, 256.0 * (1.0 - Roughness));
            specular = light.Color * spec * Specular;
        }
        
        vec3 attenuation = light.Color / (4.0 * PI * fragToLightLength * fragToLightLength);

        return (diffuse + specular) * attenuation;
    }
    return vec3(0.0);
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

float Visibility(PointShadow pointShadow)
{
    vec3 lightToFrag = vec3(inData.FragPos - lightsUBO.Lights[pointShadow.LightIndex].Position);
    float lightToFragLength = length(lightToFrag);

    float twoDist = lightToFragLength * lightToFragLength;
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;
    
    const float MIN_BIAS = EPSILON;
    const float MAX_BIAS = 1.5;
    float twoBias = mix(MAX_BIAS * MAX_BIAS, MIN_BIAS * MIN_BIAS, max(dot(Normal, lightToFrag / lightToFragLength), 0.0));

    // Map from [nearPlane; farPlane] to [0.0; 1.0]
    float mapedDepth = (twoDist - twoBias - twoNearPlane) / (twoFarPlane - twoNearPlane);
    
    const float DISK_RADIUS = 0.08;
    float shadowFactor = texture(pointShadow.Sampler, vec4(lightToFrag, mapedDepth));
    for (int i = 0; i < 20; i++)
    {
        shadowFactor += texture(pointShadow.Sampler, vec4(lightToFrag + sampleOffsetDirections[i] * DISK_RADIUS, mapedDepth));
    }

    return shadowFactor / 20.0;
}

float LinearizeDepth(float depth, float nearPlane, float farPlane)
{
    float z = depth * 2.0 - 1.0;
    return (2.0 * nearPlane * farPlane) / (farPlane + nearPlane - z * (farPlane - nearPlane));
}
