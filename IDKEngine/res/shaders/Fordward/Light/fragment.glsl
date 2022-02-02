#version 460 core
layout(location = 0) out vec4 FragColor;

in InOutVars
{
    vec3 FragColor;
} inData;

void main()
{
    FragColor = vec4(inData.FragColor, 1.0);
}