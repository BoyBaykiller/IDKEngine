#version 460 core

layout(location = 0) out vec4 FragColor;
layout(location = 2) out vec2 VelocityColor;

layout(binding = 0) uniform samplerCube SamplerSkyBoxAlbedo;

in InOutVars
{
    vec3 TexCoord;
} inData;

void main()
{    
    FragColor = texture(SamplerSkyBoxAlbedo, inData.TexCoord);
    // TODO: Maybe implement velocity and TAA?
    VelocityColor = vec2(0.0);
}