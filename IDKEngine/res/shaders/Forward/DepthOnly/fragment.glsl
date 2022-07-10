#version 460 core
#define EPSILON 0.001
#extension GL_ARB_bindless_texture : require

layout(location = 0) out vec4 FragColor;

struct Material
{
    sampler2D Albedo;
    sampler2D Normal;
    sampler2D Roughness;
    sampler2D Specular;
    sampler2D Emissive;
};

layout(std430, binding = 5) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

in InOutVars
{
    vec2 TexCoord;
    flat int MaterialIndex;
} inData;

void main()
{
    vec4 albedo = texture(materialSSBO.Materials[inData.MaterialIndex].Albedo, inData.TexCoord);
    if (albedo.a < EPSILON)
        discard;
}