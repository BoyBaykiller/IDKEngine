uint CompressUR11G11B10(vec3 data)
{
    uint r = uint(round(data.x * ((1u << 11) - 1)));
    uint g = uint(round(data.y * ((1u << 11) - 1)));
    uint b = uint(round(data.z * ((1u << 10) - 1)));

    uint compressed = (b << 22) | (g << 11) | (r << 0);

    return compressed;
}

uint CompressSR11G11B10(vec3 data)
{
    data = data * 0.5 + 0.5;
    return CompressUR11G11B10(data);
}

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

vec2 SignNotZero(vec2 v)
{
    return vec2((v.x >= 0.0) ? +1.0 : -1.0, (v.y >= 0.0) ? +1.0 : -1.0);
}

vec2 EncodeUnitVec(vec3 v)
{
    vec2 p = vec2(v.x, v.y) * (1.0 / (abs(v.x) + abs(v.y) + abs(v.z)));
    return (v.z <= 0.0) ? ((1.0 - abs(vec2(p.y, p.x))) * SignNotZero(p)) : p;
}

vec3 DecodeUnitVec(vec2 e)
{
    vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
    if (v.z < 0) v.xy = (1.0 - abs(v.yx)) * SignNotZero(v.xy);
    return normalize(v);
}
