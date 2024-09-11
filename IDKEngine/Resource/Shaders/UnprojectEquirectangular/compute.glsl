#version 460 core

AppInclude(include/Transformations.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform imageCube ImgResult;
layout(binding = 0) uniform sampler2D SamplerEquirectangular;

vec2 SampleSphericalMap(vec3 v);
vec4 SrgbToLinear(vec4 sRGB);

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);
    vec2 uv = (imgCoord.xy + 0.5) / imageSize(ImgResult);
    vec2 ndc = uv * 2.0 - 1.0;
    
    vec3 toCubemap = GetWorldSpaceDirection(ndc, imgCoord.z);
    vec2 equirectangularUV = SampleSphericalMap(toCubemap);
    vec4 color = texture(SamplerEquirectangular, equirectangularUV);

    imageStore(ImgResult, imgCoord, SrgbToLinear(color));
}

vec2 SampleSphericalMap(vec3 v)
{
    // Source: https://learnopengl.com/PBR/IBL/Diffuse-irradiance
    const vec2 invAtan = vec2(0.1591, 0.3183);

    vec2 uv = vec2(atan(v.z, v.x), asin(v.y));
    uv *= invAtan;
    uv += 0.5;
    return uv;
}

vec4 SrgbToLinear(vec4 sRGB)
{
    bvec3 cutoff = lessThan(sRGB.rgb, vec3(0.04045));
    vec3 higher = pow((sRGB.rgb + vec3(0.055)) / vec3(1.055), vec3(2.4));
    vec3 lower = sRGB.rgb / vec3(12.92);

    return vec4(mix(higher, lower, cutoff), sRGB.a);
}