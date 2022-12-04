#version 460 core

layout(binding = 0, r32ui) restrict readonly writeonly uniform uimage3D ImgVoxelsAlbedo;
layout(binding = 1, r32ui) restrict uniform uimage3D ImgFragCounter;

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
} inData;

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos);

void main()
{
    ivec3 voxelPos = WorlSpaceToVoxelImageSpace(inData.FragPos);
    uint fragCounterData = imageAtomicAdd(ImgFragCounter, voxelPos, 1u);
}

ivec3 WorlSpaceToVoxelImageSpace(vec3 worldPos)
{
    vec3 ndc = (vxgiDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz;
    ivec3 voxelPos = ivec3((ndc * 0.5 + 0.5) * imageSize(ImgVoxelsAlbedo));
    return voxelPos;
}