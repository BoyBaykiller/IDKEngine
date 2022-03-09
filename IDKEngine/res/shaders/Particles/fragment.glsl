#version 460 core
layout(location = 0) out vec4 FragColor;

in InOutVars
{
    vec3 Color;
} inData;

void main()
{
    FragColor = vec4(inData.Color, 0.25);
}