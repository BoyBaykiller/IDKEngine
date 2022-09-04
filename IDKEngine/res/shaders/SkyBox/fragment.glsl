#version 460 core
#extension GL_ARB_bindless_texture : require

layout(location = 0) out vec4 FragColor;
layout(location = 2) out vec2 VelocityColor;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

in InOutVars
{
    vec3 TexCoord;
} inData;

void main()
{    
    FragColor = texture(skyBoxUBO.Albedo, inData.TexCoord);
    // TODO: Implement velocity and TAA?
    VelocityColor = vec2(0.0);
}