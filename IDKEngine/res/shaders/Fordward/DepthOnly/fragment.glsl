#version 460 core
#define EPSILON 0.001
#extension GL_ARB_bindless_texture : require

layout(location = 0) out vec4 FragColor;

struct Material
{
    sampler2D Albedo;
    uvec2 _pad0;

    sampler2D Normal;
    uvec2 _pad1;

    sampler2D Roughness;
    uvec2 _pad3;

    sampler2D Specular;
    uvec2 _pad4;
};

layout(std140, binding = 1) uniform MaterialUBO
{
    #define GLSL_MAX_UBO_MATERIAL_COUNT 256 // used in shader and client code - keep in sync!
    Material Materials[GLSL_MAX_UBO_MATERIAL_COUNT];
} materialUBO;

in InOutVars
{
    vec2 TexCoord;
    flat int MaterialIndex;
} inData;

void main()
{
    vec4 albedo = texture(materialUBO.Materials[inData.MaterialIndex].Albedo, inData.TexCoord);
    if (albedo.a < EPSILON)
        discard;
}