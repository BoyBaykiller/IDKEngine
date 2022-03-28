#version 460 core
layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D Sampler0;

in InOutVars
{
    vec2 TexCoord;
} inData;

vec3 LinearToInverseGamma(vec3 rgb, float gamma);
vec3 ACESFilm(vec3 x);

void main()
{
    vec3 color = texture(Sampler0, inData.TexCoord).rgb;

    color = ACESFilm(color);
    color = LinearToInverseGamma(color, 2.2);

    FragColor = vec4(color, 1.0);
}

vec3 LinearToInverseGamma(vec3 rgb, float gamma)
{
    return pow(rgb, vec3(1.0 / gamma));
}

// Source: https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
vec3 ACESFilm(vec3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return (x * (a * x + b)) / (x * (c * x + d) + e);
}