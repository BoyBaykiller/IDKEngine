#version 460 core
#define EPSILON 0.001
#extension GL_ARB_bindless_texture : require

layout(location = 0) out vec4 FragColor;

in InOutVars
{
    vec2 TexCoord;
    flat sampler2D Albedo;
} inData;

void main()
{
    vec4 albedo = texture(inData.Albedo, inData.TexCoord);
    if (albedo.a < EPSILON)
        discard;
}