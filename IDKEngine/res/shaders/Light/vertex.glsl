#version 460 core
layout(location = 0) in vec3 Position;

AppInclude(shaders/include/Buffers.glsl)

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

    mat4 model = mat4(
        vec4(light.Radius, 0.0, 0.0, 0.0),
        vec4(0.0, light.Radius, 0.0, 0.0),
        vec4(0.0, 0.0, light.Radius, 0.0),
        vec4(light.Position, 1.0)
    );

    outData.LightColor = light.Color;
    outData.FragPos = (model * vec4(Position, 1.0)).xyz;
    outData.ClipPos = basicDataUBO.ProjView * vec4(outData.FragPos, 1.0);
    outData.PrevClipPos = basicDataUBO.PrevProjView * vec4(outData.FragPos, 1.0);
    outData.Position = light.Position;
    outData.Radius = light.Radius;

    uint index = taaDataUBO.Frame % taaDataUBO.Samples;
    vec2 offset = vec2(
        taaDataUBO.Jitters[index / 2][(index % 2) * 2 + 0],
        taaDataUBO.Jitters[index / 2][(index % 2) * 2 + 1]
    );

    vec4 jitteredClipPos = outData.ClipPos;
    jitteredClipPos.xy += offset * jitteredClipPos.w * taaDataUBO.Enabled;

    gl_Position = jitteredClipPos;
}