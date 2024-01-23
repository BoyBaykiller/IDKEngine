#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)

layout(location = 1) out vec4 AlbedoAlpha;
layout(location = 2) out vec4 NormalSpecular;
layout(location = 3) out vec4 EmissiveRoughness;
layout(location = 4) out vec2 Velocity;

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float TransmissionBias;
    float IORBias;
    uint MeshletsStart;
    vec3 AbsorbanceBias;
    uint MeshletCount;
};

struct Material
{
    vec3 EmissiveFactor;
    uint BaseColorFactor;

    float TransmissionFactor;
    float AlphaCutoff;
    float RoughnessFactor;
    float MetallicFactor;

    vec3 Absorbance;
    float IOR;

    sampler2D Transmission;
    uvec2 _pad0;

    sampler2D BaseColor;
    sampler2D MetallicRoughness;

    sampler2D Normal;
    sampler2D Emissive;
};

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 3) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

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
    float DeltaRenderTime;
    float Time;
} basicDataUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    float MipmapBias;
    int TemporalAntiAliasingMode;
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

in InOutVars
{
    vec2 TexCoord;
    vec4 PrevClipPos;
    vec3 Normal;
    vec3 Tangent;
    flat uint MeshID;
} inData;

void main()
{
    Mesh mesh = meshSSBO.Meshes[inData.MeshID];
    Material material = materialSSBO.Materials[mesh.MaterialIndex];
    
    float lod = textureQueryLod(material.BaseColor, inData.TexCoord).y;

    vec4 albedoAlpha = textureLod(material.BaseColor, inData.TexCoord, lod + taaDataUBO.MipmapBias) * DecompressUR8G8B8A8(material.BaseColorFactor);
    if (albedoAlpha.a < material.AlphaCutoff)
    {
        discard;
    }

    vec3 interpTangent = normalize(inData.Tangent);
    vec3 interpNormal = normalize(inData.Normal);

    mat3 tbn = GetTBN(interpTangent, interpNormal);
    vec3 textureNormal = texture(material.Normal, inData.TexCoord).rgb;
    textureNormal = tbn * normalize(textureNormal * 2.0 - 1.0);

    vec3 normal = mix(interpNormal, textureNormal, mesh.NormalMapStrength);
    vec3 emissive = texture(material.Emissive, inData.TexCoord).rgb * material.EmissiveFactor * MATERIAL_EMISSIVE_FACTOR + mesh.EmissiveBias * albedoAlpha.rgb;
    float specular = clamp(texture(material.MetallicRoughness, inData.TexCoord).r * material.MetallicFactor + mesh.SpecularBias, 0.0, 1.0);
    float roughness = clamp(texture(material.MetallicRoughness, inData.TexCoord).g * material.RoughnessFactor + mesh.RoughnessBias, 0.0, 1.0);

    AlbedoAlpha = albedoAlpha;
    NormalSpecular = vec4(normal, specular);
    EmissiveRoughness = vec4(emissive, roughness);

    vec2 uv = gl_FragCoord.xy / textureSize(gBufferDataUBO.Velocity, 0);
    vec2 thisNdc = (uv * 2.0 - 1.0) - taaDataUBO.Jitter;
    vec2 historyNdc = inData.PrevClipPos.xy / inData.PrevClipPos.w;

    Velocity = (thisNdc - historyNdc) * 0.5; // transformed to UV space [0, 1], + 0.5 cancels out
}