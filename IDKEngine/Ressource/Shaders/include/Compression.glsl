uint CompressUR11G11B10(vec3 data)
{
    uint r = uint(round(data.x * ((1u << 11) - 1)));
    uint g = uint(round(data.y * ((1u << 11) - 1)));
    uint b = uint(round(data.z * ((1u << 10) - 1)));

    uint compressed = (b << 22) | (g << 11) | (r << 0);

    return compressed;
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

uint CompressSR11G11B10(vec3 data)
{
    data = data * 0.5 + 0.5;
    return CompressUR11G11B10(data);
}
vec3 DecompressSR11G11B10(uint data)
{
    return DecompressUR11G11B10(data) * 2.0 - 1.0;
}

vec4 DecompressUR8G8B8A8(uint data)
{
    return unpackUnorm4x8(data);
}

vec4 DecompressSR8G8B8A8(uint data)
{
    return DecompressUR8G8B8A8(data) * 2.0 - 1.0;
}

vec2 OctWrap(vec2 v) {
    vec2 w = 1.0 - abs(v.yx);
    if (v.x < 0.0) w.x = -w.x;
    if (v.y < 0.0) w.y = -w.y;
    return w;
}

/// Source: https://www.shadertoy.com/view/cljGD1
// vec3 in range [-1.0, 1.0] with length=1 ->
// vec2 in range [ 0.0, 1.0]
vec2 EncodeUnitVec(vec3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    n.xy = n.z > 0.0 ? n.xy : OctWrap(n.xy);
    n.xy = n.xy * 0.5 + 0.5;
    return n.xy;
}
// vec2 in range [ 0.0, 1.0] ->
// vec3 in range [-1.0, 1.0] with length=1
vec3 DecodeUnitVec(vec2 f)
{
    f = f * 2.0 - 1.0;

    // https://twitter.com/Stubbesaurus/status/937994790553227264
    vec3 n = vec3(f.xy, 1.0 - abs(f.x) - abs(f.y));
    float t = max(-n.z, 0.0);
    n.x += n.x >= 0.0 ? -t : t;
    n.y += n.y >= 0.0 ? -t : t;
    return normalize(n);
}

// vec2 in range [0.0, 1.0] ->
// vec3 in range [-1.0, 1.0] with length=1
// This is useful for getting the normal from a normal map image
vec3 ReconstructPackedNormal(vec2 v)
{
    vec3 result;
    result.xy = v * 2.0 - 1.0;
    result.z = sqrt(max(1.0 - dot(v.rg, v.rg), 0.0));
    return result;
}
