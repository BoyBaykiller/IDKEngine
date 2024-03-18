#version 460 core

AppInclude(include/Constants.glsl)

layout(location = 0) in vec3 Position;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    int PointShadowIndex;
    vec3 PrevPosition;
    float _pad0;
};

layout(std140, binding = 2) uniform LightsUBO
{
    Light Lights[GPU_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

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
    int SampleCount;
    float MipmapBias;
    int TemporalAntiAliasingMode;
} taaDataUBO;

out InOutVars
{
    vec3 LightColor;
    vec3 FragPos;
    vec4 ClipPos;
    vec4 PrevClipPos;
    vec3 Position;
    float Radius;
} outData;

void main()
{
    Light light = lightsUBO.Lights[gl_InstanceID];
    mat4x3 modelMatrix = mat4x3(
        vec3(light.Radius, 0.0, 0.0),
        vec3(0.0, light.Radius, 0.0),
        vec3(0.0, 0.0, light.Radius),
        vec3(light.Position)
    );
    // model[0] (row0, matches OpenTK) == vec3(light.Radius, 0.0, 0.0)

    mat4x3 prevModelMatrix = mat4x3(
        vec3(light.Radius, 0.0, 0.0),
        vec3(0.0, light.Radius, 0.0),
        vec3(0.0, 0.0, light.Radius),
        vec3(light.PrevPosition)
    );

    outData.LightColor = light.Color;
    outData.FragPos = modelMatrix * vec4(Position, 1.0);
    outData.ClipPos = basicDataUBO.ProjView * vec4(outData.FragPos, 1.0);
    outData.PrevClipPos = basicDataUBO.PrevProjView * vec4(prevModelMatrix * vec4(Position, 1.0), 1.0);

    outData.Position = light.Position;
    outData.Radius = light.Radius;

    // Add jitter independent of perspective by multiplying with w
    vec4 jitteredClipPos = outData.ClipPos;
    jitteredClipPos.xy += taaDataUBO.Jitter * outData.ClipPos.w;

    gl_Position = jitteredClipPos;
}