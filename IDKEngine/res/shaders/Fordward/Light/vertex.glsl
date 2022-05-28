#version 460 core
layout(location = 0) in vec3 Position;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    float _pad0;
};

layout(std140, binding = 3) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 64 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FreezeFramesCounter;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
} basicDataUBO;

layout(std140, binding = 5) uniform TaaDataUBO
{
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    int Frame;
    float VelScale;
} taaDataUBO;

out InOutVars
{
    vec3 FragColor;
    vec3 FragPos;
    vec4 ClipPos;
    vec4 PrevClipPos;
    flat int LightIndex;
    flat vec3 Position;
    flat float Radius;
} outData;

void main()
{
    Light light = lightsUBO.Lights[gl_InstanceID];

    mat4 model = mat4(
        vec4(light.Radius, 0.0, 0.0, 0.0),
        vec4(0.0, light.Radius, 0.0, 0.0),
        vec4(0.0, 0.0, light.Radius, 0.0),
        vec4(light.Position, 1.0)
    );


    outData.FragColor = light.Color;
    outData.LightIndex = gl_InstanceID;
    outData.FragPos = (model * vec4(Position, 1.0)).xyz;
    outData.ClipPos = basicDataUBO.ProjView * vec4(outData.FragPos, 1.0);
    outData.PrevClipPos = basicDataUBO.PrevProjView * vec4(outData.FragPos, 1.0);
    outData.Position = light.Position;
    outData.Radius = light.Radius;

    int rawIndex = taaDataUBO.Frame % taaDataUBO.Samples;
    vec2 offset = vec2(
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) * 2 + 0],
        taaDataUBO.Jitters[rawIndex / 2][(rawIndex % 2) * 2 + 1]
    );

    vec4 jitteredClipPos = basicDataUBO.ProjView * model * vec4(Position, 1.0);
    jitteredClipPos.xy += offset * jitteredClipPos.w * taaDataUBO.Enabled;

    gl_Position = jitteredClipPos;
}