# IDK_BC5_normal_metallicRoughness

## Contributors

* Julian, [@BoyBaykiller](https://github.com/BoyBaykiller)
* LVSTRI, [@LVSTRI](https://github.com/LVSTRI)

## Status

Draft

## Dependencies

Written against the glTF 2.0 spec. Requires `KHR_texture_basisu`.

## Overview

The `KHR_texture_basisu` extension allows to specify textures in KTX v2 format with Basis Universal supercompression. When such a texture is loaded the engine transcodes it into one of the many block-compressed formats supported by various GPU APIs. The [specification](https://registry.khronos.org/DataFormat/specs/1.3/dataformat.1.3.html) offers "individual" and "differential" encoding. For textures with uncorrelated color channels, "individual" encoding is preferable, as it provides higher quality.

The "individual" block-compressed `BC5_RG` format is especially useful for normal and metallicRoughness textures, however it expects the source data to be placed in the red and alpha channel of the image which is not how the glTF spec defines these textures to be layed out. This forces engines to use a less optimal format such as `BC7_RGBA` with "differential" encoding.

When this extension is used, it means that normal and metallicRoughness have been compressed with data in the R and A channels, allowing engines to transcode them to `BC5_RG`.

## Details

Transcoded metallicRoughness textures contain the metalness values in the R channel and roughness values in the G channel.

Transcoded normal textures contain the X and Y components in the R and G channels respectively.
The 3 component vector can then be reconstructed as follows:
```glsl
vec3 ReconstructNormal(vec2 v)
{
    vec3 result;
    result.xy = v * 2.0 - 1.0;
    result.z = sqrt(max(1.0 - dot(v.rg, v.rg), 0.0));
    return normalize(result);
}
```

## Implementation note

If [basis_universal](https://github.com/BinomialLLC/basis_universal) compressor is used, the uncompressed data can easily be placed in the correct channels by using the swizzle setting.

```cpp
if (NormalTexture)
{
    // Put X in RGB and Y in A.
    m_swizzle[0] = 0;
    m_swizzle[1] = 0;
    m_swizzle[2] = 0;
    m_swizzle[3] = 1;
}
if (MetallicRoughnessTexture)
{
    // Put metalness in RGB and roughness in A.
    m_swizzle[0] = 2;
    m_swizzle[1] = 2;
    m_swizzle[2] = 2;
    m_swizzle[3] = 1;
}
```

## Known Implementations

* https://github.com/BoyBaykiller/meshoptimizer

## Resources

* [KTX-Software, BC5 transcoding](https://github.com/KhronosGroup/KTX-Software/blob/0d1ebc1d0859cdaa699f68491f00ec1c02a33b7c/lib/basisu/transcoder/basisu_transcoder.cpp#L8934)
* [libktx reference](https://github.khronos.org/KTX-Software/libktx/ktx_8h.html#a30cc58c576392303d9a5a54b57ef29b5)