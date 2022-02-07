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
    Light Lights[128];
    int LightCount;
} lightsUBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

out InOutVars
{
    vec3 FragColor;
} outData;

void main()
{
    Light light = lightsUBO.Lights[gl_InstanceID];
    outData.FragColor = light.Color;

    mat4 model = mat4(
        vec4(light.Radius, 0.0, 0.0, 0.0),
        vec4(0.0, light.Radius, 0.0, 0.0),
        vec4(0.0, 0.0, light.Radius, 0.0),
        vec4(light.Position, 1.0)
    );

    gl_Position = basicDataUBO.ProjView * model * vec4(Position, 1.0);
}