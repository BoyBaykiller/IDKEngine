#version 460 core
#define EPSILON 0.001
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)

layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D SamplerAO;
layout(binding = 1) uniform sampler2D SamplerIndirectLighting;


struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    int PointShadowIndex;
};

struct PointShadow
{
    samplerCube Texture;
    samplerCubeShadow ShadowTexture;

    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    vec3 _pad0;
    float FarPlane;
};

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    float _pad0;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
} shadowDataUBO;

layout(std140, binding = 2) uniform LightsUBO
{
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

vec3 GetBlinnPhongLighting(Light light, vec3 viewDir, vec3 normal, vec3 albedo, float specular, float roughness, vec3 sampleToLight);
float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightToSample);
vec3 NDCToWorld(vec3 ndc);

uniform bool IsVXGI;

in InOutVars
{
    vec2 TexCoord;
} inData;

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);
    vec2 uv = inData.TexCoord;
    
    float depth = textureLod(gBufferDataUBO.Depth, uv, 0.0).r;
    if (depth == 1.0)
    {
        FragColor = vec4(0.0);
        return;
    }

    vec4 albedoAlpha = textureLod(gBufferDataUBO.AlbedoAlpha, uv, 0.0);
    vec4 normalSpecular = textureLod(gBufferDataUBO.NormalSpecular, uv, 0.0);
    vec4 emissiveRoughness = textureLod(gBufferDataUBO.EmissiveRoughness, uv, 0.0);

    vec3 ndc = vec3(uv, depth) * 2.0 - 1.0;
    vec3 fragPos = NDCToWorld(ndc);

    vec3 albedo = albedoAlpha.rgb;
    vec3 normal = normalSpecular.rgb;
    float specular = normalSpecular.a;
    vec3 emissive = emissiveRoughness.rgb;
    float roughness = emissiveRoughness.a;

    vec3 viewDir = normalize(fragPos - basicDataUBO.ViewPos);

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];

        vec3 sampleToLight = light.Position - fragPos;
        vec3 contrib = GetBlinnPhongLighting(light, viewDir, normal, albedo, specular, roughness, sampleToLight);
        if (light.PointShadowIndex >= 0)
        {
            PointShadow pointShadow = shadowDataUBO.PointShadows[light.PointShadowIndex];
            contrib *= Visibility(pointShadow, normal, -sampleToLight);
        }
        directLighting += contrib;
    }

    vec3 indirectLight;
    if (IsVXGI)
    {
        indirectLight = texture(SamplerIndirectLighting, uv).rgb * albedo;
    }
    else
    {
        indirectLight = vec3(0.03) * albedo;
    }
    float ambientOcclusion = 1.0 - texture(SamplerAO, uv).r;

    FragColor = vec4((directLighting + indirectLight) * ambientOcclusion + emissive, albedoAlpha.a);
}

vec3 GetBlinnPhongLighting(Light light, vec3 viewDir, vec3 normal, vec3 albedo, float specular, float roughness, vec3 sampleToLight)
{
    float fragToLightLength = length(sampleToLight);

    vec3 lightDir = sampleToLight / fragToLightLength;
    float cosTerm = dot(normal, lightDir);
    if (cosTerm > 0.0)
    {
        vec3 diffuseContrib = light.Color * cosTerm * albedo;  
    
        vec3 specularContrib = vec3(0.0);
        vec3 halfwayDir = normalize(lightDir + -viewDir);
        float temp = dot(normal, halfwayDir);
        // TODO: Implement not shit lighting that doesnt break under some conditions
        if (!IsVXGI && temp > 0.0)
        {
            float spec = pow(temp, 256.0 * (1.0 - roughness));
            specularContrib = light.Color * spec * specular;
        }
        
        vec3 attenuation = light.Color / (4.0 * PI * fragToLightLength * fragToLightLength);

        return (diffuseContrib + specularContrib) * attenuation;
    }
    return vec3(0.0);
}

// Source: https://learnopengl.com/Advanced-Lighting/Shadows/Point-Shadows
const vec3 SHADOW_SAMPLE_OFFSETS[] =
{
   vec3( 1.0,  1.0,  1.0 ), vec3(  1.0, -1.0,  1.0 ), vec3( -1.0, -1.0,  1.0 ), vec3( -1.0,  1.0,  1.0 ), 
   vec3( 1.0,  1.0, -1.0 ), vec3(  1.0, -1.0, -1.0 ), vec3( -1.0, -1.0, -1.0 ), vec3( -1.0,  1.0, -1.0 ),
   vec3( 1.0,  1.0,  0.0 ), vec3(  1.0, -1.0,  0.0 ), vec3( -1.0, -1.0,  0.0 ), vec3( -1.0,  1.0,  0.0 ),
   vec3( 1.0,  0.0,  1.0 ), vec3( -1.0,  0.0,  1.0 ), vec3(  1.0,  0.0, -1.0 ), vec3( -1.0,  0.0, -1.0 ),
   vec3( 0.0,  1.0,  1.0 ), vec3(  0.0, -1.0,  1.0 ), vec3(  0.0, -1.0, -1.0 ), vec3(  0.0,  1.0, -1.0 )
};

float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightToSample)
{
    float lightToFragLength = length(lightToSample);

    float twoDist = lightToFragLength * lightToFragLength;
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;
    
    const float MIN_BIAS = EPSILON;
    const float MAX_BIAS = 1.5;
    float twoBias = mix(MAX_BIAS * MAX_BIAS, MIN_BIAS * MIN_BIAS, max(dot(normal, lightToSample / lightToFragLength), 0.0));

    // Map from [nearPlane, farPlane] to [0.0, 1.0]
    float mapedDepth = (twoDist - twoBias - twoNearPlane) / (twoFarPlane - twoNearPlane);
    
    const float DISK_RADIUS = 0.08;
    float shadowFactor = texture(pointShadow.ShadowTexture, vec4(lightToSample, mapedDepth));
    for (int i = 0; i < SHADOW_SAMPLE_OFFSETS.length(); i++)
    {
        shadowFactor += texture(pointShadow.ShadowTexture, vec4(lightToSample + SHADOW_SAMPLE_OFFSETS[i] * DISK_RADIUS, mapedDepth));
    }

    return shadowFactor / 21.0;
}

vec3 NDCToWorld(vec3 ndc)
{
    vec4 worldPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);
    return worldPos.xyz / worldPos.w;
}