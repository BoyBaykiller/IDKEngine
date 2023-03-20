#version 460 core
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxelsAlbedo;
layout(binding = 1) uniform sampler2D SamplerAO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    float _pad0;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    uint Frame;
    float VelScale;
} taaDataUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

layout(std140, binding = 5) uniform VoxelizerDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} voxelizerDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

vec3 IndirectLight(vec3 point, vec3 incomming, vec3 normal, float specularChance, float roughness);
float GetMaterialVariance(float specularChance, float roughness);
vec4 TraceCone(vec3 start, vec3 direction, vec3 normal, float coneAngle, float stepMultiplier);
vec3 UniformSampleSphere(float rnd0, float rnd1);
vec3 CosineSampleHemisphere(vec3 normal, float rnd0, float rnd1);
float InterleavedGradientNoise(vec2 imgCoord, uint index);
vec3 NDCToWorld(vec3 ndc);

uniform float NormalRayOffset;
uniform int MaxSamples;
uniform float GIBoost;
uniform float GISkyBoxBoost;
uniform float StepMultiplier;
uniform bool IsTemporalAccumulation;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    float depth = texture(gBufferDataUBO.Depth, uv).r;
    if (depth == 1.0)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }

    vec3 fragPos = NDCToWorld(vec3(uv, depth) * 2.0 - 1.0);
    vec3 normal = texture(gBufferDataUBO.NormalSpecular, uv).rgb;
    float specular = texture(gBufferDataUBO.NormalSpecular, uv).a;
    float roughness = texture(gBufferDataUBO.EmissiveRoughness, uv).a;

    vec3 viewDir = fragPos - basicDataUBO.ViewPos;
    vec3 indirectLight = IndirectLight(fragPos, viewDir, normal, specular, roughness) * GIBoost;

    float ambientOcclusion = 1.0 - texture(SamplerAO, uv).r;
    indirectLight *= ambientOcclusion;

    imageStore(ImgResult, imgCoord, vec4(indirectLight, 1.0));
}

vec3 IndirectLight(vec3 point, vec3 incomming, vec3 normal, float specularChance, float roughness)
{
    vec3 irradiance = vec3(0.0);
    float materialVariance = GetMaterialVariance(specularChance, roughness);
    uint samples = uint(mix(1.0, float(MaxSamples), materialVariance));

    uint noiseIndex = IsTemporalAccumulation ? (taaDataUBO.Frame % taaDataUBO.Samples) * MaxSamples : 0u;
    for (uint i = 0; i < samples; i++)
    {
        float rnd0 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 0);
        float rnd1 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 1);
        float rnd2 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 2);
        noiseIndex++;
        
        vec3 dir = CosineSampleHemisphere(normal, rnd0, rnd1);

        const float maxConeAngle = 0.32;
        const float minConeAngle = 0.005;
        float coneAngle;
        if (specularChance > rnd2)
        {
            vec3 reflectionDir = reflect(incomming, normal);
            reflectionDir = normalize(mix(reflectionDir, dir, roughness));
            dir = reflectionDir;
            
            coneAngle = mix(minConeAngle, maxConeAngle, roughness);
        }
        else
        {
            coneAngle = maxConeAngle;
        }

        vec4 coneTrace = TraceCone(point, dir, normal, coneAngle, StepMultiplier);
        coneTrace += (1.0 - coneTrace.a) * (texture(skyBoxUBO.Albedo, dir) * GISkyBoxBoost);
        
        irradiance += coneTrace.rgb;
    }
    irradiance /= float(samples);

    return irradiance;
}

float GetMaterialVariance(float specularChance, float roughness)
{
    float diffuseChance = 1.0 - specularChance;
    float perceivedFinalRoughness = 1.0 - (specularChance * (1.0 - roughness));
    return mix(perceivedFinalRoughness, 1.0, diffuseChance);
}

vec4 TraceCone(vec3 start, vec3 direction, vec3 normal, float coneAngle, float stepMultiplier)
{
    vec3 voxelGridWorldSpaceSize = voxelizerDataUBO.GridMax - voxelizerDataUBO.GridMin;
    vec3 voxelWorldSpaceSize = voxelGridWorldSpaceSize / textureSize(SamplerVoxelsAlbedo, 0);
    float voxelMaxLength = max(voxelWorldSpaceSize.x, max(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    float voxelMinLength = min(voxelWorldSpaceSize.x, min(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    uint maxLevel = textureQueryLevels(SamplerVoxelsAlbedo) - 1;
    vec4 accumlatedColor = vec4(0.0);

    start += normal * voxelMaxLength * NormalRayOffset;

    float distFromStart = voxelMaxLength;
    while (accumlatedColor.a < 0.99)
    {
        float coneDiameter = 2.0 * tan(coneAngle) * distFromStart;
        float sampleDiameter = max(voxelMinLength, coneDiameter);
        float sampleLod = log2(sampleDiameter / voxelMinLength);
        
        vec3 worldPos = start + direction * distFromStart;
        vec3 sampleUVT = (voxelizerDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz * 0.5 + 0.5;
        if (any(lessThan(sampleUVT, vec3(0.0))) || any(greaterThanEqual(sampleUVT, vec3(1.0))) || sampleLod > maxLevel)
        {
            break;
        }
        vec4 sampleColor = textureLod(SamplerVoxelsAlbedo, sampleUVT, sampleLod);

        accumlatedColor += (1.0 - accumlatedColor.a) * sampleColor;
        distFromStart += sampleDiameter * stepMultiplier;
    }

    return accumlatedColor;
}

vec3 UniformSampleSphere(float rnd0, float rnd1)
{
    float z = rnd0 * 2.0 - 1.0;
    float a = rnd1 * 2.0 * PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);

    return vec3(x, y, z);
}

// Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
vec3 CosineSampleHemisphere(vec3 normal, float rnd0, float rnd1)
{
    // Convert unit vector in sphere to a cosine weighted vector in hemisphere
    return normalize(normal + UniformSampleSphere(rnd0, rnd1));
}

// Source: https://www.shadertoy.com/view/WsfBDf
float InterleavedGradientNoise(vec2 imgCoord, uint index)
{
    imgCoord += float(index) * 5.588238;
    return fract(52.9829189 * fract(0.06711056 * imgCoord.x + 0.00583715 * imgCoord.y));
}

vec3 NDCToWorld(vec3 ndc)
{
    vec4 viewPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);
    return viewPos.xyz / viewPos.w;
}
