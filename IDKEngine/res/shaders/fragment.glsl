#version 460 core
layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D Sampler0;
layout(binding = 1) uniform sampler2D Sampler1;
layout(binding = 2) uniform sampler2D Sampler2;

vec3 LinearToInverseGamma(vec3 rgb, float gamma);
vec3 Uncharted2Tonemap(vec3 x);
vec3 TonemapUncharted2(vec3 color);
vec3 ACESFilm(vec3 x);

in InOutVars
{
    vec2 TexCoord;
} inData;

void main()
{
    vec3 color = texture(Sampler0, inData.TexCoord).rgb;
    color += texture(Sampler1, inData.TexCoord).rgb;
    color += texture(Sampler2, inData.TexCoord).rgb;
    
    color = ACESFilm(color);
    color = LinearToInverseGamma(color, 2.2);
    
    FragColor = vec4(color, 1.0);
}

vec3 LinearToInverseGamma(vec3 rgb, float gamma)
{
    return pow(rgb, vec3(1.0 / gamma));
}

vec3 Uncharted2Tonemap(vec3 x)
{
	const float A = 0.15;
	const float B = 0.50;
	const float C = 0.10;
	const float D = 0.20;
	const float E = 0.02;
	const float F = 0.30;
	return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

vec3 TonemapUncharted2(vec3 color)
{
	const float W = 11.2;
    const float exposure = 1.0;
	vec3 curr = Uncharted2Tonemap(exposure * color);
	vec3 whiteScale = 1.0 / Uncharted2Tonemap(vec3(W));
	return curr * whiteScale;
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
