#version 460 core

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Pbr.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 1) restrict writeonly uniform image2D ImgResultDepth;

layout(std140, binding = 7) uniform SettingsUBO
{
    vec3 Absorbance;
    int SampleCount;
    float Scattering;
    float MaxDist;
    float Strength;
} settingsUBO;

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep, int sampleCount);
bool Shadow(PointShadow pointShadow, vec3 lightToSample);
float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpaceSamplePos);
float ComputeScattering(float cosTheta, float scaterring);

// Source: http://www.alexandre-pestana.com/volumetric-lights/
const float DitherPattern[][4] = 
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

    float depth = texture(gBufferDataUBO.Depth, uv).r;
    vec3 ndc = vec3(uv * 2.0 - 1.0, depth);
    vec3 unjitteredFragPos = PerspectiveTransform(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), perFrameDataUBO.InvProjView);
    vec3 viewToFrag = unjitteredFragPos - perFrameDataUBO.ViewPos;

    float viewToFragLen = length(viewToFrag);
    vec3 viewDir = viewToFrag / viewToFragLen;
    
    if (viewToFragLen > settingsUBO.MaxDist)
    {
        viewToFrag = viewDir * settingsUBO.MaxDist;
    }

    vec3 deltaStep = viewToFrag / settingsUBO.SampleCount;
    float randomJitter = DitherPattern[imgCoord.x % DitherPattern[0].length()][imgCoord.y % DitherPattern.length()];
    vec3 origin = perFrameDataUBO.ViewPos + deltaStep * randomJitter;

    vec3 scattered = vec3(0.0);
    for (int i = 0; i < shadowsUBO.Count; i++)
    {
        PointShadow pointShadow = shadowsUBO.PointShadows[i];
        Light light = lightsUBO.Lights[pointShadow.LightIndex];

        scattered += UniformScatter(light, pointShadow, origin, viewDir, deltaStep, settingsUBO.SampleCount);
    }

    imageStore(ImgResult, imgCoord, vec4(scattered * settingsUBO.Strength, 1.0));
    imageStore(ImgResultDepth, imgCoord, vec4(depth));
}

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep, int sampleCount)
{
    vec3 scattered = vec3(0.0);
    vec3 samplePoint = origin;
    for (int i = 0; i < sampleCount; i++)
    {
        vec3 lightToSample = samplePoint - light.Position;
        if (!Shadow(pointShadow, lightToSample))
        {
            float lengthToLight = length(lightToSample);
            float attenuation = GetAttenuationFactor(lengthToLight * lengthToLight, light.Radius);
            
            vec3 absorbed = exp(-settingsUBO.Absorbance * lengthToLight);
            
            vec3 lightDir = lightToSample / lengthToLight;
            float cosTheta = dot(lightDir, -viewDir);
            
            scattered += light.Color * ComputeScattering(cosTheta, settingsUBO.Scattering) * attenuation * absorbed;
        }

        samplePoint += deltaStep;
    }
    scattered /= sampleCount;

    // Apply Beers's law, Absorbance is constant so we can have it outside the loop
    vec3 absorbed = exp(-settingsUBO.Absorbance * length(origin - samplePoint));
    scattered *= absorbed;
    
    return scattered;
}

// Only binaries shadow because soft shadows are not worth it in this case
bool Shadow(PointShadow pointShadow, vec3 lightToSample)
{
    float depth = GetLightSpaceDepth(pointShadow, lightToSample);
    float closestDepth = texture(pointShadow.ShadowMapTexture, lightToSample).r;

    return depth > closestDepth;
}

float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpaceSamplePos)
{
    float dist = max(abs(lightSpaceSamplePos.x), max(abs(lightSpaceSamplePos.y), abs(lightSpaceSamplePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}

float ComputeScattering(float cosTheta, float scaterring)
{
    // Mie scaterring approximated with Henyey-Greenstein phase function
    // Source: http://www.alexandre-pestana.com/volumetric-lights/
    return (1.0 - scaterring * scaterring) / (4.0 * PI * pow(1.0 + scaterring * scaterring - 2.0 * scaterring * cosTheta, 1.5));
}
