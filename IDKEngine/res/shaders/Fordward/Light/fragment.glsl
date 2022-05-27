#version 460 core
#define ENTITY_BIFIELD_BITS_FOR_TYPE 3 // used in shader and client code - keep in sync!
#define ENTITY_TYPE_LIGHT 2u << (16 - ENTITY_BIFIELD_BITS_FOR_TYPE) // used in shader and client code - keep in sync!

layout(location = 0) out vec4 FragColor;
layout(location = 1) out vec4 NormalSpecColor;
layout(location = 2) out uint MeshIndexColor;
layout(location = 3) out vec2 VelocityColor;

in InOutVars
{
    vec3 FragColor;
    flat int LightIndex;
} inData;

void main()
{
    FragColor = vec4(inData.FragColor, 1.0);
    NormalSpecColor = vec4(0.0);
    MeshIndexColor = inData.LightIndex | ENTITY_TYPE_LIGHT;
    VelocityColor = vec2(0.0);
}