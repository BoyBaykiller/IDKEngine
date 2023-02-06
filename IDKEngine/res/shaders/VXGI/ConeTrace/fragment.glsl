#version 460 core
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require

layout(location = 0) out vec4 FragColor;
layout(binding = 0) uniform sampler3D SamplerVoxelsAlbedo;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
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

vec4 TraceCone(vec3 start, vec3 normal, vec3 direction, float coneAngle, float stepMultiplier);
vec3 IndirectLight(vec3 start, vec3 normal, vec3 debug);
uint GetPCGHash(inout uint seed);
float GetRandomFloat01();
vec3 UniformSampleSphere(float rnd0, float rnd1);
vec3 CosineSampleHemisphere(vec3 normal, float rnd0, float rnd1);
vec3 NDCToWorld(vec3 ndc);

uniform float NormalRayOffset;
uniform int MaxSamples;
uniform float GIBoost;
uniform float GISkyBoxBoost;
uniform float StepMultiplier;

uint rngSeed;

in InOutVars
{
    vec2 TexCoord;
} inData;

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);
    vec2 uv = inData.TexCoord;

    float depth = texture(gBufferDataUBO.Depth, uv).r;
    if (depth == 1.0)
    {
        FragColor = vec4(0.0);
        return;
    }

    rngSeed = imgCoord.x * 312 + imgCoord.y * 291 * taaDataUBO.Frame;
    //rngSeed = 0;

    vec3 fragPos = NDCToWorld(vec3(uv, depth) * 2.0 - 1.0);
    vec3 normal = texture(gBufferDataUBO.NormalSpecular, uv).rgb;

    vec3 viewDir = fragPos - basicDataUBO.ViewPos; 
    vec3 indirectLight = IndirectLight(fragPos, normal, reflect(viewDir, normal)) * GIBoost;

    FragColor = vec4(indirectLight, 1.0);
}

vec4 TraceCone(vec3 start, vec3 normal, vec3 direction, float coneAngle, float stepMultiplier)
{
    vec3 voxelGridWorlSpaceSize = voxelizerDataUBO.GridMax - voxelizerDataUBO.GridMin;
    vec3 voxelWorldSpaceSize = voxelGridWorlSpaceSize / textureSize(SamplerVoxelsAlbedo, 0);
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
            accumlatedColor += (1.0 - accumlatedColor.a) * (texture(skyBoxUBO.Albedo, direction) * GISkyBoxBoost);
            break;
        }
        vec4 sampleColor = textureLod(SamplerVoxelsAlbedo, sampleUVT, sampleLod);

        accumlatedColor += (1.0 - accumlatedColor.a) * sampleColor;
        distFromStart += sampleDiameter * stepMultiplier;
    }

    return accumlatedColor;
}

vec3 IndirectLight(vec3 start, vec3 normal, vec3 debug)
{
    // return TraceCone(start, normal, debug, 0.0, 0.2).rgb;

    vec3 diffuse = vec3(0.0);
    for (int i = 0; i < MaxSamples; i++)
    {
        vec3 dir = CosineSampleHemisphere(normal, GetRandomFloat01(), GetRandomFloat01());
        diffuse += TraceCone(start, normal, dir, 0.32, StepMultiplier).rgb;
    }
    diffuse /= float(MaxSamples);
    debug /= float(MaxSamples);
    return diffuse;
}

// Faster and much more random than Wang Hash
// Source: https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/
uint GetPCGHash(inout uint seed)
{
    seed = seed * 747796405u + 2891336453u;
    uint word = ((seed >> ((seed >> 28u) + 4u)) ^ seed) * 277803737u;
    return (word >> 22u) ^ word;
}

float GetRandomFloat01()
{
    return float(GetPCGHash(rngSeed)) / 4294967296.0;
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

vec3 NDCToWorld(vec3 ndc)
{
    vec4 viewPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);
    return viewPos.xyz / viewPos.w;
}
