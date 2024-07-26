#version 460 core

AppInclude(include/Compression.glsl)

layout(location = 0) out vec4 OutFragColor;
layout(location = 1) out vec2 OutNormal;
layout(location = 2) out vec3 OutEmissive;
layout(location = 3) out vec2 OutVelocity;

in InOutData
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
    OutFragColor = vec4(inData.LightColor, 1.0);
    OutNormal = EncodeUnitVec((inData.FragPos - inData.Position) / inData.Radius);
    OutEmissive = OutFragColor.rgb;
    
    vec2 thisNdc = inData.ClipPos.xy / inData.ClipPos.w;
    vec2 historyNdc = inData.PrevClipPos.xy / inData.PrevClipPos.w;
    OutVelocity = (thisNdc - historyNdc) * 0.5; // transformed to UV space [0, 1], + 0.5 cancels out
}