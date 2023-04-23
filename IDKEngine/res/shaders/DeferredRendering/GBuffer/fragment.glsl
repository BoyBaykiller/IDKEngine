#version 460 core
layout(early_fragment_tests) in;

layout(location = 1) out vec4 AlbedoAlpha;
layout(location = 2) out vec4 NormalSpecular;
layout(location = 3) out vec4 EmissiveRoughness;
layout(location = 4) out vec2 Velocity;

AppInclude(shaders/include/Buffers.glsl)

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
    
    vec4 albedoAlpha = texture(material.BaseColor, inData.TexCoord) * unpackUnorm4x8(material.BaseColorFactor);
    vec3 emissive = (texture(material.Emissive, inData.TexCoord).rgb * material.EmissiveFactor) + inData.EmissiveBias * albedoAlpha.rgb;
    vec3 normal = texture(material.Normal, inData.TexCoord).rgb;
    normal = inData.TangentToWorld * normalize(normal * 2.0 - 1.0);
    normal = normalize(mix(normalize(inData.Normal), normal, inData.NormalMapStrength));

    float specular = clamp(texture(material.MetallicRoughness, inData.TexCoord).r * material.MetallicFactor + inData.SpecularBias, 0.0, 1.0);
    float roughness = clamp(texture(material.MetallicRoughness, inData.TexCoord).g * material.RoughnessFactor + inData.RoughnessBias, 0.0, 1.0);

    AlbedoAlpha = albedoAlpha;
    NormalSpecular = vec4(normal, specular);
    EmissiveRoughness = vec4(emissive, roughness); 

    vec2 uv = (inData.ClipPos.xy / inData.ClipPos.w) * 0.5 + 0.5;
    vec2 prevUV = (inData.PrevClipPos.xy / inData.PrevClipPos.w) * 0.5 + 0.5;
    Velocity = (uv - prevUV) * taaDataUBO.VelScale;
}