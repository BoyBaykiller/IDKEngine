#version 460 core
#define EMISSIVE_MATERIAL_MULTIPLIER 5.0
#extension GL_ARB_bindless_texture : require
layout(early_fragment_tests) in;

layout(location = 1) out vec4 AlbedoAlpha;
layout(location = 2) out vec4 NormalSpecular;
layout(location = 3) out vec4 EmissiveRoughness;
layout(location = 4) out vec2 Velocity;


struct Material
{
    sampler2D Albedo;
    sampler2D Normal;
    sampler2D Roughness;
    sampler2D Specular;
    sampler2D Emissive;
};

layout(std430, binding = 5) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
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

layout(std140, binding = 3) uniform TaaDataUBO
{
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    uint Frame;
    float VelScale;
} taaDataUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

in InOutVars
{
    vec2 TexCoord;
    vec4 ClipPos;
    vec4 PrevClipPos;
    vec3 Normal;
    mat3 TangentToWorld;
    flat uint MaterialIndex;
    flat float EmissiveBias;
    flat float NormalMapStrength;
    flat float SpecularBias;
    flat float RoughnessBias;
} inData;

void main()
{
    Material material = materialSSBO.Materials[inData.MaterialIndex];
    
    vec4 albedo = texture(material.Albedo, inData.TexCoord);
    vec3 emissive = (texture(material.Emissive, inData.TexCoord).rgb * EMISSIVE_MATERIAL_MULTIPLIER + inData.EmissiveBias) * albedo.rgb;
    vec3 normal = texture(material.Normal, inData.TexCoord).rgb;
    normal = inData.TangentToWorld * normalize(normal * 2.0 - 1.0);
    normal = normalize(mix(normalize(inData.Normal), normal, inData.NormalMapStrength));
    
    float roughness = clamp(texture(material.Roughness, inData.TexCoord).r + inData.RoughnessBias, 0.0, 1.0);
    float specular = clamp(texture(material.Specular, inData.TexCoord).r + inData.SpecularBias, 0.0, 1.0);

    AlbedoAlpha = albedo;
    NormalSpecular = vec4(normal, specular);
    EmissiveRoughness = vec4(emissive, roughness); 

    vec2 uv = (inData.ClipPos.xy / inData.ClipPos.w) * 0.5 + 0.5;
    vec2 prevUV = (inData.PrevClipPos.xy / inData.PrevClipPos.w) * 0.5 + 0.5;
    Velocity = (uv - prevUV) * taaDataUBO.VelScale;
}