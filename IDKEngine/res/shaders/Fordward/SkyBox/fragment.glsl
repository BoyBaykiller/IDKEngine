#version 460 core
#define MESHES_CLEAR_COLOR -1

layout(location = 0) out vec4 FragColor;
layout(location = 2) out int MeshIndexColor;
layout(location = 3) out vec2 VelocityColor;

layout(binding = 0) uniform samplerCube SamplerEnvAlbedo;

in InOutVars
{
    vec3 TexCoord;
} inData;

void main()
{    
    FragColor = texture(SamplerEnvAlbedo, inData.TexCoord);
    MeshIndexColor = MESHES_CLEAR_COLOR;
    // TODO: Maybe implement velocity and TAA?
    VelocityColor = vec2(0.0);
}