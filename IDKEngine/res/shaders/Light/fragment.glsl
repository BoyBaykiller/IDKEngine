#version 460 core

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 AlbedoAlpha;
layout(location = 2) out vec4 NormalSpecular;
layout(location = 3) out vec4 EmissiveRoughness;
layout(location = 4) out vec2 Velocity;

AppInclude(shaders/include/Buffers.glsl)

in InOutVars
{
    vec3 LightColor;
    vec3 FragPos;
    vec4 ClipPos;
    vec4 PrevClipPos;
    flat vec3 Position;
    flat float Radius;
} inData;

void main()
{
    FragColor = vec4(inData.LightColor, 1.0);
    AlbedoAlpha = vec4(0.0, 0.0, 0.0, 1.0);
    NormalSpecular = vec4((inData.FragPos - inData.Position) / inData.Radius, 0.0);
    EmissiveRoughness = vec4(FragColor.rgb, 1.0);

    vec2 uv = (inData.ClipPos.xy / inData.ClipPos.w) * 0.5 + 0.5;
    vec2 prevUV = (inData.PrevClipPos.xy / inData.PrevClipPos.w) * 0.5 + 0.5;
    Velocity = (uv - prevUV) * taaDataUBO.VelScale;
}