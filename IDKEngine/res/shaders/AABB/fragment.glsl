#version 460 core
#define ENTITY_TYPE_NONE 0u // used in shader and client code - keep in sync!

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 NormalSpecColor;
layout(location = 2) out uint MeshIndexColor;
layout(location = 3) out vec2 VelocityColor;

void main()
{
    FragColor = vec4(0.0, 1.0, 0.0, 1.0);
    NormalSpecColor = vec4(0.0);
    MeshIndexColor = ENTITY_TYPE_NONE;
    VelocityColor = vec2(0.0);
}