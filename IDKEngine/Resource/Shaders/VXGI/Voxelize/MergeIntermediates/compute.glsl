#version 460 core
// This shader is only used when TAKE_ATOMIC_FP16_PATH is false (GL_NV_shader_atomic_fp16_vector is not supported)
// In this case the voxelizer atomically writes to 3 seperate r32ui textures
// instead of only one rgba16f texture. Here we merge them together into one.

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

layout(binding = 0) restrict uniform image3D ImgResult;

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