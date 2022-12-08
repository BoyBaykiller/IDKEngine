#version 460 core

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

layout(binding = 0) restrict writeonly uniform image3D ImgVoxelsAlbedo;
layout(binding = 1) restrict writeonly uniform uimage3D ImgFragCounter;

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);

    imageStore(ImgVoxelsAlbedo, imgCoord, vec4(0.0));
    imageStore(ImgFragCounter, imgCoord, uvec4(0u));
}