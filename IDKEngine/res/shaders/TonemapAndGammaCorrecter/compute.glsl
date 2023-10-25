#version 460 core

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D Sampler0;
layout(binding = 1) uniform sampler2D Sampler1;
layout(binding = 2) uniform sampler2D Sampler2;

vec3 LinearToSrgb(vec3 rgb, float gamma);
vec3 ACESFilm(vec3 x);

// Source: https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/globals.hlsli#L824
const float BayerMatrix8[][8] =
{
	{ 1.0 / 65.0, 49.0 / 65.0, 13.0 / 65.0, 61.0 / 65.0, 4.0 / 65.0, 52.0 / 65.0, 16.0 / 65.0, 64.0 / 65.0 },
	{ 33.0 / 65.0, 17.0 / 65.0, 45.0 / 65.0, 29.0 / 65.0, 36.0 / 65.0, 20.0 / 65.0, 48.0 / 65.0, 32.0 / 65.0 },
	{ 9.0 / 65.0, 57.0 / 65.0, 5.0 / 65.0, 53.0 / 65.0, 12.0 / 65.0, 60.0 / 65.0, 8.0 / 65.0, 56.0 / 65.0 },
	{ 41.0 / 65.0, 25.0 / 65.0, 37.0 / 65.0, 21.0 / 65.0, 44.0 / 65.0, 28.0 / 65.0, 40.0 / 65.0, 24.0 / 65.0 },
	{ 3.0 / 65.0, 51.0 / 65.0, 15.0 / 65.0, 63.0 / 65.0, 2.0 / 65.0, 50.0 / 65.0, 14.0 / 65.0, 62.0 / 65.0 },
	{ 35.0 / 65.0, 19.0 / 65.0, 47.0 / 65.0, 31.0 / 65.0, 34.0 / 65.0, 18.0 / 65.0, 46.0 / 65.0, 30.0 / 65.0 },
	{ 11.0 / 65.0, 59.0 / 65.0, 7.0 / 65.0, 55.0 / 65.0, 10.0 / 65.0, 58.0 / 65.0, 6.0 / 65.0, 54.0 / 65.0 },
	{ 43.0 / 65.0, 27.0 / 65.0, 39.0 / 65.0, 23.0 / 65.0, 42.0 / 65.0, 26.0 / 65.0, 38.0 / 65.0, 22.0 / 65.0 }
};

uniform bool IsDithering;
uniform float Gamma;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 hdrColor = texture(Sampler0, uv).rgb;
    hdrColor += texture(Sampler1, uv).rgb;
    hdrColor += texture(Sampler2, uv).rgb;

    vec3 ldrColor = ACESFilm(hdrColor);
    vec3 srgbColor = LinearToSrgb(ldrColor, Gamma);

    if (IsDithering)
    {
        int x = int(imgCoord.x) % BayerMatrix8[0].length();
        int y = int(imgCoord.y) % BayerMatrix8.length();

        srgbColor += (BayerMatrix8[x][y] - 0.5) / (BayerMatrix8.length() * BayerMatrix8[0].length());
    }

    imageStore(ImgResult, imgCoord, vec4(srgbColor, 1.0));
}

vec3 LinearToSrgb(vec3 rgb, float gamma)
{
    return pow(rgb, vec3(1.0 / gamma));
}

// Source: https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
vec3 ACESFilm(vec3 x)
{
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return (x * (a * x + b)) / (x * (c * x + d) + e);
}
