#version 460 core
#extension GL_ARB_bindless_texture : require
#extension GL_NV_shader_atomic_fp16_vector : enable
#ifdef GL_NV_shader_atomic_fp16_vector
#extension GL_NV_gpu_shader5 : require
#endif

#ifdef GL_NV_shader_atomic_fp16_vector
layout(binding = 0, rgba16f) restrict uniform image3D ImgVoxelsAlbedo;
#else
layout(binding = 0, r32ui) restrict uniform uimage3D ImgVoxelsAlbedo;
#endif

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
    centroid vec3 FragPos;
    centroid vec2 TexCoord;
    centroid vec3 Normal;
    flat uint MaterialIndex;
} inData;

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos);

void main()
{
    ivec3 voxelPos = WorlSpaceToVoxelImageSpace(inData.FragPos);

    Material material = materialSSBO.Materials[inData.MaterialIndex];
    vec4 albedo = texture(material.Albedo, inData.TexCoord);

    uint fragCounter = imageLoad(ImgFragCounter, voxelPos).x;
    float avgMultiplier = 1.0 / float(fragCounter);

    vec4 normalizedAlbedo = albedo * avgMultiplier;
#ifdef GL_NV_shader_atomic_fp16_vector
    imageAtomicAdd(ImgVoxelsAlbedo, voxelPos, f16vec4(normalizedAlbedo));
    // imageAtomicMax(ImgVoxelsAlbedo, voxelPos, f16vec4(normalizedAlbedo));
#else
    ivec4 quantizedAlbedoRgba = ivec4(normalizedAlbedo * 255.0);
    uint packedAlbedo = (quantizedAlbedoRgba.a << 24) | (quantizedAlbedoRgba.b << 16) | (quantizedAlbedoRgba.g << 8) | (quantizedAlbedoRgba.r << 0);
    imageAtomicAdd(ImgVoxelsAlbedo, voxelPos, packedAlbedo);
    // imageAtomicMax(ImgVoxelsAlbedo, voxelPos, packedAlbedo);
#endif

}

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos)
{
    vec3 ndc = (vxgiDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz;
    ivec3 voxelPos = ivec3((ndc * 0.5 + 0.5) * imageSize(ImgVoxelsAlbedo));
    return voxelPos;
}