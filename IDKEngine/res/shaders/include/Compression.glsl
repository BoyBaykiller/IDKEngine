#ifndef Compression_H
#define Compression_H

vec3 DecompressUR11G11B10(uint data)
{
    float r = (data >> 0)  & ((1u << 11) - 1);
    float g = (data >> 11) & ((1u << 11) - 1);
    float b = (data >> 22) & ((1u << 10) - 1);

    r /= (1u << 11) - 1;
    g /= (1u << 11) - 1;
    b /= (1u << 10) - 1;

    return vec3(r, g, b);
}

vec3 DecompressSR11G11B10(uint data)
{
    return DecompressUR11G11B10(data) * 2.0 - 1.0;
}

vec4 DecompressUR8G8B8A8(uint data)
{
    return unpackUnorm4x8(data);
}

#endif