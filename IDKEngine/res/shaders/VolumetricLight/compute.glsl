#version 460 core
#define PI 3.1415926536
#extension GL_ARB_bindless_texture : require

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 1) uniform sampler2D SamplerDepth;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    float _pad0;
};

struct PointShadow
{
    samplerCube Sampler;
    float NearPlane;
    float FarPlane;

    mat4 ProjViewMatrices[6];

    vec3 _pad0;
    int LightIndex;
};

layout(std140, binding = 2) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    #define GLSL_MAX_UBO_POINT_SHADOW_COUNT 16 // used in shader and client code - keep in sync!
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FreezeFrameCounter;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep);
bool Shadow(PointShadow pointShadow, vec3 lightToSample);
vec3 NDCToWorldSpace(vec3 ndc);
float ComputeScattering(float lightDotView);

// Source: https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/globals.hlsli#L824
const float BayerMatrix8[8][8] =
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

uniform int Samples;
uniform float Scattering;
uniform float MaxDist;
uniform float Strength;
uniform vec3 Absorbance;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 ndc = vec3(uv, texture(SamplerDepth, uv).r) * 2.0 - 1.0;
    vec3 viewToFrag = NDCToWorldSpace(ndc) - basicDataUBO.ViewPos;
    float viewToFragLen = length(viewToFrag);
    vec3 viewDir = viewToFrag / viewToFragLen;
    
    if (viewToFragLen > MaxDist)
        viewToFrag = viewDir * MaxDist;

    float offset = BayerMatrix8[imgCoord.x % BayerMatrix8.length()][imgCoord.y % BayerMatrix8.length()];
    vec3 deltaStep = viewToFrag / Samples;
    vec3 origin = basicDataUBO.ViewPos + deltaStep * offset;

    vec3 scattered = vec3(0.0);
    for (int i = 0; i < shadowDataUBO.Count; i++)
    {
        PointShadow pointShadow = shadowDataUBO.PointShadows[i];
        Light light = lightsUBO.Lights[pointShadow.LightIndex];

        scattered += UniformScatter(light, pointShadow, origin, viewDir, deltaStep);
    }

    imageStore(ImgResult, imgCoord, vec4(scattered * Strength, 1.0));
}

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep)
{
    vec3 scattered = vec3(0.0);
    vec3 samplePoint = origin;
    for (int i = 0; i < Samples; i++)
    {
        vec3 lightToSample = samplePoint - light.Position;
        if (!Shadow(pointShadow, lightToSample))
        {
            float lengthToLight = length(lightToSample);
            vec3 power = light.Color / (4.0 * PI * dot(lightToSample, lightToSample));
            
            // Apply Beers's law
            vec3 lightDir = lightToSample / lengthToLight;
            vec3 absorbed = exp(-Absorbance * lengthToLight);
            scattered += ComputeScattering(dot(lightDir, -viewDir)) * power * absorbed;
        }

        samplePoint += deltaStep;
    }
    // Apply Beers's law, Absorbance is constant so we can have it outside the loop
    vec3 absorbed = exp(-Absorbance * length(origin - samplePoint));
    scattered *= absorbed;
    
    return scattered / Samples;
}

// Only binaries shadow because soft shadows are not worth it in this case
bool Shadow(PointShadow pointShadow, vec3 lightToSample)
{
    float twoDist = dot(lightToSample, lightToSample);
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;
    float twoBias = -1.0;

    // Map from [nearPlane; farPlane] to [0.0; 1.0]
    float mapedDepth = (twoDist - twoBias - twoNearPlane) / (twoFarPlane - twoNearPlane);
    // Texture lookups with no shadowsampler but comparison mode to != None on is actually UB
    // Works on both my nvidia and amd card though
    float closestDepth = texture(pointShadow.Sampler, lightToSample).r;

    return mapedDepth > closestDepth;
}

vec3 NDCToWorldSpace(vec3 ndc)
{
    vec4 worldPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);
    return worldPos.xyz / worldPos.w;
}

// Mie scaterring approximated with Henyey-Greenstein phase function
// Source: http://www.alexandre-pestana.com/volumetric-lights/
float ComputeScattering(float lightDotView)
{
    return (1.0 - Scattering * Scattering) / (4.0 * PI * pow(1.0 + Scattering * Scattering - 2.0 * Scattering * lightDotView, 1.5));
}
