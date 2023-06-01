#version 460 core
#extension GL_ARB_bindless_texture : require

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 AlbedoAlpha;
layout(location = 2) out vec4 NormalSpecular;
layout(location = 3) out vec4 EmissiveRoughness;
layout(location = 4) out vec2 Velocity;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
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
    vec3 TexCoord;
    vec4 ClipPos;
    vec4 PrevClipPos;
} inData;

void main()
{
    FragColor = textureLod(skyBoxUBO.Albedo, inData.TexCoord, 0.0);
    
    AlbedoAlpha = vec4(0.0);
    NormalSpecular = vec4(0.0);
    EmissiveRoughness = vec4(FragColor.rgb, 1.0);

    vec2 uv = (inData.ClipPos.xy / inData.ClipPos.w) * 0.5 + 0.5;
    vec2 prevUV = (inData.PrevClipPos.xy / inData.PrevClipPos.w) * 0.5 + 0.5;
    Velocity = (uv - prevUV) * taaDataUBO.VelScale;
}