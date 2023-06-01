#version 460 core

layout(local_size_x = 4, local_size_y = 4, local_size_z = 4) in;

layout(binding = 0) restrict writeonly uniform image3D ImgResult;
layout(binding = 0) uniform sampler3D SamplerDownsample;

layout(location = 0) uniform int Lod;

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);
    ivec3 imgSize = imageSize(ImgResult);
    vec3 uvw = (imgCoord + 0.5) / imgSize;

    vec4 result = textureLod(SamplerDownsample, uvw, Lod);

    result += textureLodOffset(SamplerDownsample, uvw, Lod, ivec3(-1,  0,  0));
    result += textureLodOffset(SamplerDownsample, uvw, Lod, ivec3( 1,  0,  0));

    result += textureLodOffset(SamplerDownsample, uvw, Lod, ivec3( 0, -1,  0));
    result += textureLodOffset(SamplerDownsample, uvw, Lod, ivec3( 0,  1,  0));

    result += textureLodOffset(SamplerDownsample, uvw, Lod, ivec3( 0,  0, -1));
    result += textureLodOffset(SamplerDownsample, uvw, Lod, ivec3( 0,  0,  1));

    result /= 7.0;

    imageStore(ImgResult, imgCoord, result);
}