#version 460 core
#extension GL_ARB_bindless_texture : require
#define PI 3.1415926536

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerDepth;

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

layout(std140, binding = 3) uniform LightsUBO
{
    Light Lights[128];
    int LightCount;
} lightsUBO;

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[8];
    int PointCount;
} shadowDataUBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep);
bool Shadow(PointShadow pointShadow, vec3 lightToSample);
vec3 NDCToWorldSpace(vec3 ndc);
float ComputeScattering(float lightDotView);

// From: http://www.alexandre-pestana.com/volumetric-lights/
const float ditherPattern[4][4] = 
{
    { 0.0, 0.5, 0.125, 0.625 },
    { 0.75, 0.22, 0.875, 0.375 },
    { 0.1875, 0.6875, 0.0625, 0.5625 },
    { 0.9375, 0.4375, 0.8125, 0.3125 }
};

uniform float Scattering;
uniform float MaxDist;
uniform int Samples;
uniform int RendererdFrame;

void main()
{

    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    if (shadowDataUBO.PointCount == 0)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }

    vec2 imgResultSize = imageSize(ImgResult);
    vec2 uv = imgCoord / imgResultSize;

    vec3 ndc = vec3(uv, texture(SamplerDepth, uv).r) * 2.0 - 1.0;
    vec3 viewToFrag = NDCToWorldSpace(ndc) - basicDataUBO.ViewPos;
    float viewToFragLen = length(viewToFrag);
    vec3 viewDir = viewToFrag / viewToFragLen;
    
    if (viewToFragLen > MaxDist)
        viewToFrag = viewDir * MaxDist;

    vec3 deltaStep = viewToFrag / Samples;
    vec3 origin = basicDataUBO.ViewPos + deltaStep * ditherPattern[imgCoord.x % 4][imgCoord.y % 4];

    vec3 scattered = vec3(0.0);
    for (int i = 0; i < shadowDataUBO.PointCount; i++)
    {
        PointShadow pointShadow = shadowDataUBO.PointShadows[i];
        Light light = lightsUBO.Lights[pointShadow.LightIndex];

        scattered += UniformScatter(light, pointShadow, origin, viewDir, deltaStep);
    }
    scattered /= shadowDataUBO.PointCount;

    imageStore(ImgResult, imgCoord, vec4(scattered, 1.0));
}

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep)
{
    const vec3 MEDIUM_ABSORBANCE = vec3(0.025);
    vec3 scattered = vec3(0.0);
    vec3 samplePoint = origin;
    for (int i = 0; i < Samples; i++)
    {
        vec3 lightToSample = samplePoint - light.Position;
        if (!Shadow(pointShadow, lightToSample))
        {
            float lengthToLight = length(lightToSample);
            vec3 lightDir = lightToSample / lengthToLight;
            vec3 power = light.Color / dot(lightToSample, lightToSample);
            
            // Apply Beers's law
            vec3 absorbed = exp(-MEDIUM_ABSORBANCE * lengthToLight);
            scattered += ComputeScattering(dot(lightDir, -viewDir)) * power * absorbed;
        }

        samplePoint += deltaStep;
    }
    // Apply Beers's law, Absorbance is constant so we can have it outside the loop
    vec3 absorbed = exp(-MEDIUM_ABSORBANCE * distance(origin, samplePoint));
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
    float closestDepth = texture(pointShadow.Sampler, lightToSample).r;

    return mapedDepth > closestDepth;
}

vec3 NDCToWorldSpace(vec3 ndc)
{
    vec4 worldPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);

    return worldPos.xyz / worldPos.w;
}

// Mie scaterring approximated with Henyey-Greenstein phase function from http://www.alexandre-pestana.com/volumetric-lights/
float ComputeScattering(float lightDotView)
{
    return (1.0 - Scattering * Scattering) / (4.0 * PI * pow(1.0 + Scattering * Scattering - 2.0 * Scattering * lightDotView, 1.5));
}

