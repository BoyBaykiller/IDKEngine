#version 460 core

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 NormalSpecColor;
layout(location = 2) out vec2 VelocityColor;

layout(std140, binding = 3) uniform TaaDataUBO
{
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    int Frame;
    float VelScale;
} taaDataUBO;

in InOutVars
{
    vec3 FragColor;
    vec3 FragPos;
    vec4 ClipPos;
    vec4 PrevClipPos;
    flat vec3 Position;
    flat float Radius;
} inData;

void main()
{
    FragColor = vec4(inData.FragColor, 1.0);
    NormalSpecColor = vec4((inData.FragPos - inData.Position) / inData.Radius, 1.0);

    vec2 uv = (inData.ClipPos.xy / inData.ClipPos.w) * 0.5 + 0.5;
    vec2 prevUV = (inData.PrevClipPos.xy / inData.PrevClipPos.w) * 0.5 + 0.5;
    VelocityColor = (uv - prevUV) * taaDataUBO.VelScale;
}