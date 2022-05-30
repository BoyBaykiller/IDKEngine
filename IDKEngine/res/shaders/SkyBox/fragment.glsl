#version 460 core
#define ENTITY_TYPE_NONE 0u // used in shader and client code - keep in sync!

layout(location = 0) out vec4 FragColor;
layout(location = 2) out uint MeshIndexColor;
layout(location = 3) out vec2 VelocityColor;

layout(binding = 0) uniform samplerCube SamplerEnvAlbedo;

in InOutVars
{
    vec3 TexCoord;
} inData;

void main()
{    
    FragColor = texture(SamplerEnvAlbedo, inData.TexCoord);
    MeshIndexColor = ENTITY_TYPE_NONE;
    // TODO: Maybe implement velocity and TAA?
    VelocityColor = vec2(0.0);
}