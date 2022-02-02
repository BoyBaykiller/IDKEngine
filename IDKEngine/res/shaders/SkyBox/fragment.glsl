#version 460 core

layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform samplerCube SamplerEnvAlbedo;

in InOutVars
{
    vec3 TexCoord;
} inData;

void main()
{    
    FragColor = texture(SamplerEnvAlbedo, inData.TexCoord);
}