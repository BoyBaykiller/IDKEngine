#version 460 core

layout(location = 0) out vec4 OutBoxColor;

void main()
{
    const vec4 boxColor = vec4(0.0, 1.0, 0.0, 1.0);
    OutBoxColor = boxColor;
}