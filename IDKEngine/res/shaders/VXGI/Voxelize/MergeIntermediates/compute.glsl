#version 460 core
#extension GL_NV_shader_atomic_fp16_vector : enable
#if GL_NV_shader_atomic_fp16_vector
#error "This GPU supports GL_NV_shader_atomic_fp16_vector. There is no need to use or compile this shader"
#endif

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

layout(binding = 0, rgba16f) restrict uniform image3D ImgResult;

layout(binding = 0) uniform sampler3D SamplerVoxelsR;
layout(binding = 1) uniform sampler3D SamplerVoxelsG;
layout(binding = 2) uniform sampler3D SamplerVoxelsB;

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);

    float a = imageLoad(ImgResult, imgCoord).a;
    if (a > 0.0)
    {
        float r = texelFetch(SamplerVoxelsR, imgCoord, 0).r;
        float g = texelFetch(SamplerVoxelsG, imgCoord, 0).r;
        float b = texelFetch(SamplerVoxelsB, imgCoord, 0).r;
        imageStore(ImgResult, imgCoord, vec4(r, g, b, a));
    }
}