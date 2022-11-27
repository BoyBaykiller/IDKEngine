#version 460 core

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

layout(binding = 0, r32ui) restrict uniform uimage3D ImgVoxelsAlbedo;
layout(binding = 1, r32ui) restrict uniform uimage3D ImgFragCounter;

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);

    uint data = imageLoad(ImgFragCounter, imgCoord).x;
    uint prevCount = data & ((1u << 16u) - 1u);
    const uint thisCount = 0u; // clear
    imageStore(ImgFragCounter, imgCoord, uvec4((prevCount << 16) | thisCount));

    imageStore(ImgVoxelsAlbedo, imgCoord, uvec4(0u));
}