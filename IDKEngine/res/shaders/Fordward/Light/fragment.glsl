#version 460 core
layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 NormalSpecColor;
layout(location = 2) out int MeshIndexColor;
layout(location = 3) out vec2 VelocityColor;

in InOutVars
{
    vec3 FragColor;
} inData;

void main()
{
    FragColor = vec4(inData.FragColor, 1.0);
    NormalSpecColor = vec4(0.0);
    MeshIndexColor = -1;
    VelocityColor = vec2(0.0);
}