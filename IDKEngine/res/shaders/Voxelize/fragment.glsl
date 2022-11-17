#version 460 core
#extension GL_ARB_bindless_texture : require

layout(binding = 0, rgba16f) restrict uniform image3D ImgVoxels;

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

layout(std140, binding = 5) uniform VXGIDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} vxgiDataUBO;

in InOutVars
{
    vec2 TexCoord;
    vec3 FragPos;
    flat uint MaterialIndex;
} inData;

void main()
{
    Material material = materialSSBO.Materials[inData.MaterialIndex];
    vec4 albedo = texture(material.Albedo, inData.TexCoord);

    vec3 gridExtents = vxgiDataUBO.GridMax - vxgiDataUBO.GridMin;
    ivec3 voxelPos = ivec3((inData.FragPos - vxgiDataUBO.GridMin) / gridExtents * imageSize(ImgVoxels));
    imageStore(ImgVoxels, voxelPos, albedo);
}
