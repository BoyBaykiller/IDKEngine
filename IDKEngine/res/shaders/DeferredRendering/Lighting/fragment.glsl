#version 460 core
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)

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
    uint Frame;
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

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    float MipmapBias;
} taaDataUBO;

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
float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightSpacePos);
float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpacePos);

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

    vec3 ndc = UvDepthToNdc(vec3(uv, depth));
    vec3 fragPos = NdcToWorldSpace(ndc, basicDataUBO.InvProjView);
    vec3 unjitteredFragPos = NdcToWorldSpace(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), basicDataUBO.InvProjView);

    vec3 albedo = textureLod(gBufferDataUBO.AlbedoAlpha, uv, 0.0).rgb;
    float alpha = textureLod(gBufferDataUBO.AlbedoAlpha, uv, 0.0).a;
    vec3 normal = textureLod(gBufferDataUBO.NormalSpecular, uv, 0.0).rgb;
    float specular = textureLod(gBufferDataUBO.NormalSpecular, uv, 0.0).a;
    vec3 emissive = textureLod(gBufferDataUBO.EmissiveRoughness, uv, 0.0).rgb;
    float roughness = textureLod(gBufferDataUBO.EmissiveRoughness, uv, 0.0).a;

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
            vec3 lightSpacePos = unjitteredFragPos - light.Position;
            contrib *= Visibility(pointShadow, normal, lightSpacePos);
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

    FragColor = vec4((directLighting + indirectLight) * ambientOcclusion + emissive, alpha);
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
const vec3 ShadowSampleOffsets[] =
{
    vec3( 0.0,  0.0,  0.0 ),
    vec3( 1.0,  1.0,  1.0 ), vec3(  1.0, -1.0,  1.0 ), vec3( -1.0, -1.0,  1.0 ), vec3( -1.0,  1.0,  1.0 ), 
    vec3( 1.0,  1.0, -1.0 ), vec3(  1.0, -1.0, -1.0 ), vec3( -1.0, -1.0, -1.0 ), vec3( -1.0,  1.0, -1.0 ),
    vec3( 1.0,  1.0,  0.0 ), vec3(  1.0, -1.0,  0.0 ), vec3( -1.0, -1.0,  0.0 ), vec3( -1.0,  1.0,  0.0 ),
    vec3( 1.0,  0.0,  1.0 ), vec3( -1.0,  0.0,  1.0 ), vec3(  1.0,  0.0, -1.0 ), vec3( -1.0,  0.0, -1.0 ),
    vec3( 0.0,  1.0,  1.0 ), vec3(  0.0, -1.0,  1.0 ), vec3(  0.0, -1.0, -1.0 ), vec3(  0.0,  1.0, -1.0 )
};

float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightSpacePos)
{
    float bias = 0.018;

    float visibilityFactor = 0.0;
    const float sampleDiskRadius = 0.04;
    for (int i = 0; i < ShadowSampleOffsets.length(); i++)
    {
        vec3 samplePos = (lightSpacePos + ShadowSampleOffsets[i] * sampleDiskRadius);
        float depth = GetLightSpaceDepth(pointShadow, samplePos * (1.0 - bias));
        visibilityFactor += texture(pointShadow.ShadowTexture, vec4(samplePos, depth));
    }
    visibilityFactor /= ShadowSampleOffsets.length();

    return visibilityFactor;
}

float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpacePos)
{
    float dist = max(abs(lightSpacePos.x), max(abs(lightSpacePos.y), abs(lightSpacePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}