#version 460 core
#define PI 3.1415926536

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;

AppInclude(shaders/include/Buffers.glsl)

vec3 UniformScatter(Light light, PointShadow pointShadow, vec3 origin, vec3 viewDir, vec3 deltaStep);
bool Shadow(PointShadow pointShadow, vec3 lightToSample);
vec3 NDCToWorld(vec3 ndc);
float ComputeScattering(float lightDotView);
float InterleavedGradientNoise(vec2 imgCoord, uint index);

uniform int Samples;
uniform float Scattering;
uniform float MaxDist;
uniform float Strength;
uniform vec3 Absorbance;
uniform bool IsTemporalAccumulation;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    vec3 ndc = vec3(uv, texture(gBufferDataUBO.Depth, uv).r) * 2.0 - 1.0;
    vec3 viewToFrag = NDCToWorld(ndc) - basicDataUBO.ViewPos;
    float viewToFragLen = length(viewToFrag);
    vec3 viewDir = viewToFrag / viewToFragLen;
    
    if (viewToFragLen > MaxDist)
        viewToFrag = viewDir * MaxDist;

    vec3 deltaStep = viewToFrag / Samples;
    float offset = InterleavedGradientNoise(imgCoord, IsTemporalAccumulation ? (taaDataUBO.Frame % taaDataUBO.Samples) : 0u);
    vec3 origin = basicDataUBO.ViewPos + deltaStep * offset;

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
bool Shadow(PointShadow pointShadow, vec3 lightToSample)
{
    float twoDist = dot(lightToSample, lightToSample);
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;

    // Map from [nearPlane; farPlane] to [0.0; 1.0]
    float mapedDepth = (twoDist - twoNearPlane) / (twoFarPlane - twoNearPlane);
    float closestDepth = texture(pointShadow.Texture, lightToSample).r;

    return mapedDepth > closestDepth;
}

vec3 NDCToWorld(vec3 ndc)
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

// Source: https://www.shadertoy.com/view/WsfBDf
float InterleavedGradientNoise(vec2 imgCoord, uint index) 
{
    imgCoord += float(index) * 5.588238;
    return fract(52.9829189 * fract(0.06711056 * imgCoord.x + 0.00583715 * imgCoord.y));
}
