#version 460 core

AppInclude(include/StaticUniformBuffers.glsl)

layout(location = 0) out vec4 OutFragColor;
layout(location = 1) out vec2 OutVelocity;

in InOutData
{
    vec3 TexCoord;
    vec4 ClipPos;
    vec4 PrevClipPos;
} inData;

void main()
{
    OutFragColor = textureLod(skyBoxUBO.Albedo, inData.TexCoord, 0.0);

    vec2 thisNdc = inData.ClipPos.xy / inData.ClipPos.w;
    vec2 historyNdc = inData.PrevClipPos.xy / inData.PrevClipPos.w;
    OutVelocity = (thisNdc - historyNdc) * 0.5; // transformed to UV space [0, 1], + 0.5 cancels out
}