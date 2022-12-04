#version 460 core
#extension GL_NV_shader_atomic_fp16_vector : enable
#ifdef GL_NV_shader_atomic_fp16_vector
#extension GL_NV_gpu_shader5 : require
#endif

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

#ifdef GL_NV_shader_atomic_fp16_vector
layout(binding = 0) restrict writeonly uniform image3D ImgVoxelsAlbedo;
#else
layout(binding = 0) restrict writeonly uniform uimage3D ImgVoxelsAlbedo;
#endif
layout(binding = 1, r32ui) restrict uniform uimage3D ImgFragCounter;

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);

    imageStore(ImgFragCounter, imgCoord, uvec4(0u));

#ifdef GL_NV_shader_atomic_fp16_vector
    imageStore(ImgVoxelsAlbedo, imgCoord, f16vec4(0.0));
#else
    imageStore(ImgVoxelsAlbedo, imgCoord, uvec4(0u));
#endif
}