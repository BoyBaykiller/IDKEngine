#version 460 core
layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 NormalSpecColor;
layout(location = 2) out uint MeshIndexColor;
layout(location = 3) out vec2 VelocityColor;

void main()
{
    FragColor = vec4(0.0, 1.0, 0.0, 1.0);
    NormalSpecColor = vec4(0.0);
    MeshIndexColor = -1;
    VelocityColor = vec2(0.0);
}