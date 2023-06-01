#version 460 core
#extension GL_NV_shader_atomic_fp16_vector : enable

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

layout(binding = 0, rgba16f) restrict uniform image3D ImgResult;

#if !defined GL_NV_shader_atomic_fp16_vector
layout(binding = 1) restrict writeonly uniform uimage3D ImgResultR;
layout(binding = 2) restrict writeonly uniform uimage3D ImgResultG;
layout(binding = 3) restrict writeonly uniform uimage3D ImgResultB;
#endif

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);

#if GL_NV_shader_atomic_fp16_vector

    imageStore(ImgResult, imgCoord, vec4(0.0));

#else

    float alpha = imageLoad(ImgResult, imgCoord).a;
    if (alpha > 0.0)
    {
        imageStore(ImgResultR, imgCoord, uvec4(0));
        imageStore(ImgResultG, imgCoord, uvec4(0));
        imageStore(ImgResultB, imgCoord, uvec4(0));
        imageStore(ImgResult, imgCoord, vec4(0.0));
    }

#endif
}