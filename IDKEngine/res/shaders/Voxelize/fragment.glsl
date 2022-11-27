#version 460 core
#extension GL_ARB_bindless_texture : require

layout(binding = 0, r32ui) restrict uniform uimage3D ImgVoxelsAlbedo;
layout(binding = 1, r32ui) restrict uniform uimage3D ImgFragCounter;

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

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos);

void main()
{
    Material material = materialSSBO.Materials[inData.MaterialIndex];
    vec4 albedo = texture(material.Albedo, inData.TexCoord);

    ivec3 voxelPos = WorlSpaceToVoxelImageSpace(inData.FragPos);

    uint fragCounterData = imageLoad(ImgFragCounter, voxelPos).x;
    uint prevFragCount = fragCounterData >> 16;
    float avgMultiplier = 1.0 / float(prevFragCount);

    ivec4 quantizedAlbedoRgba = ivec4(albedo * avgMultiplier * 255.0);

    uint packedAlbedo = (quantizedAlbedoRgba.a << 24) | (quantizedAlbedoRgba.b << 16) | (quantizedAlbedoRgba.g << 8) | (quantizedAlbedoRgba.r << 0);
    imageAtomicAdd(ImgVoxelsAlbedo, voxelPos, packedAlbedo);
    imageAtomicAdd(ImgFragCounter, voxelPos, 1);
}

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos)
{
    vec3 ndc = (vxgiDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz;
    ivec3 voxelPos = ivec3((ndc * 0.5 + 0.5) * imageSize(ImgVoxelsAlbedo));
    return voxelPos;
}