#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Random.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    int PointShadowIndex;
};

struct PointShadow
{
    samplerCube Texture;
    samplerCubeShadow ShadowTexture;

    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    vec3 _pad0;
    float FarPlane;
};

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    uint Frame;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

layout(std140, binding = 2) uniform LightsUBO
{
    Light Lights[GPU_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    float MipmapBias;
    int TemporalAntiAliasingMode;
} taaDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep);
bool Shadow(PointShadow pointShadow, vec3 lightSpacePos);
float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpacePos);
float ComputeScattering(float lightDotView);

uniform int Samples;
uniform float Scattering;
uniform float MaxDist;
uniform float Strength;
uniform vec3 Absorbance;

// From: http://www.alexandre-pestana.com/volumetric-lights/
const float DitherPattern[4][4] = 
{
    { 0.0, 0.5, 0.125, 0.625 },
    { 0.75, 0.22, 0.875, 0.375 },
    { 0.1875, 0.6875, 0.0625, 0.5625 },
    { 0.9375, 0.4375, 0.8125, 0.3125 }
};

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 ndc = vec3(uv * 2.0 - 1.0, texture(gBufferDataUBO.Depth, uv).r);
    vec3 unjitteredFragPos = PerspectiveTransform(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), basicDataUBO.InvProjView);
    vec3 viewToFrag = unjitteredFragPos - basicDataUBO.ViewPos;

    float viewToFragLen = length(viewToFrag);
    vec3 viewDir = viewToFrag / viewToFragLen;
    
    if (viewToFragLen > MaxDist)
    {
        viewToFrag = viewDir * MaxDist;
    }

    vec3 deltaStep = viewToFrag / Samples;
    float randomJitter = DitherPattern[imgCoord.x % DitherPattern[0].length()][imgCoord.y % DitherPattern.length()];
    vec3 origin = basicDataUBO.ViewPos + deltaStep * randomJitter;

    vec3 scattered = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];

        if (light.PointShadowIndex >= 0)
        {
            PointShadow pointShadow = shadowDataUBO.PointShadows[light.PointShadowIndex];
            scattered += UniformScatter(light, pointShadow, origin, viewDir, deltaStep);
        }
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
bool Shadow(PointShadow pointShadow, vec3 lightSpacePos)
{
    float depth = GetLightSpaceDepth(pointShadow, lightSpacePos);
    float closestDepth = texture(pointShadow.Texture, lightSpacePos).r;

    return depth > closestDepth;
}

float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpacePos)
{
    float dist = max(abs(lightSpacePos.x), max(abs(lightSpacePos.y), abs(lightSpacePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}

// Mie scaterring approximated with Henyey-Greenstein phase function
// Source: http://www.alexandre-pestana.com/volumetric-lights/
float ComputeScattering(float lightDotView)
{
    return (1.0 - Scattering * Scattering) / (4.0 * PI * pow(1.0 + Scattering * Scattering - 2.0 * Scattering * lightDotView, 1.5));
}
