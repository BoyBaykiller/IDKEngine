#version 460 core

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D Sampler0;
layout(binding = 1) uniform sampler2D Sampler1;
layout(binding = 2) uniform sampler2D Sampler2;
layout(binding = 3) uniform sampler2D Sampler3;

vec3 LinearToInverseGamma(vec3 rgb, float gamma);
vec3 ACESFilm(vec3 x);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 color = texture(Sampler0, uv).rgb;
    color += texture(Sampler1, uv).rgb;
    color += texture(Sampler2, uv).rgb;
    color += texture(Sampler3, uv).rgb;

    color = ACESFilm(color);
    color = LinearToInverseGamma(color, 2.2);

    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}

vec3 LinearToInverseGamma(vec3 rgb, float gamma)
{
    return pow(rgb, vec3(1.0 / gamma));
}

// ACES tone mapping curve fit to go from HDR to LDR
// https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
vec3 ACESFilm(vec3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return (x * (a * x + b)) / (x * (c * x + d) + e);
}
