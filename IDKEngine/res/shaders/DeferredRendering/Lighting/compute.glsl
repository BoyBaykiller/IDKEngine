#version 460 core
#define EPSILON 0.001
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerAO;
layout(binding = 1) uniform sampler3D SamplerVoxelsAlbedo;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    float _pad0;
};

struct PointShadow
{
    samplerCube Texture;
    samplerCubeShadow ShadowTexture;
    
    mat4 ProjViewMatrices[6];

    float NearPlane;
    float FarPlane;
    int LightIndex;
    float _pad0;
};

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

layout(std140, binding = 1) uniform ShadowDataUBO
{
    #define GLSL_MAX_UBO_POINT_SHADOW_COUNT 16 // used in shader and client code - keep in sync!
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int PointCount;
} shadowDataUBO;

layout(std140, binding = 2) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

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

// Faster and much more random than Wang Hash
// Source: https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/
vec4 TraceCone(vec3 start, vec3 normal, vec3 direction, float coneAngle, float stepMultiplier);
vec3 IndirectLight(vec3 start, vec3 normal, vec3 debug);
uint GetPCGHash(inout uint seed);
float GetRandomFloat01();
vec3 UniformSampleSphere(float rnd0, float rnd1);
vec3 CosineSampleHemisphere(vec3 normal, float rnd0, float rnd1);
vec3 GetBlinnPhongLighting(Light light, vec3 viewDir, vec3 normal, vec3 albedo, float specular, float roughness, vec3 sampleToLight);
float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightToSample);
vec3 NDCToWorldSpace(vec3 ndc);

// provisionally make seperate class (shader in that case) that does cone tracing using g buffer data
uniform float NormalRayOffset;
uniform int MaxSamples;
uniform float GIBoost;
uniform float GISkyBoxBoost;
uniform float StepMultiplier;
uniform bool IsVXGI;

uint rngSeed;


void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);
    
    uint rawIndex = taaDataUBO.Frame;
    rngSeed = imgCoord.x * 312 + imgCoord.y * 291 * rawIndex;

    float depth = texture(gBufferDataUBO.Depth, uv).r;
    if (depth == 1.0)
    {
        return;
    }

    float ambientOcclusion = 1.0 - texture(SamplerAO, uv).r;
    vec4 albedoAlpha = texture(gBufferDataUBO.AlbedoAlpha, uv);
    vec4 normalSpecular = texture(gBufferDataUBO.NormalSpecular, uv);
    vec4 emissiveRoughness = texture(gBufferDataUBO.EmissiveRoughness, uv);

    vec3 ndc = vec3(uv, depth) * 2.0 - 1.0;
    vec3 fragPos = NDCToWorldSpace(ndc);

    vec3 albedo = albedoAlpha.rgb;
    vec3 normal = normalSpecular.rgb;
    float specular = normalSpecular.a;
    vec3 emissive = emissiveRoughness.rgb;
    float roughness = emissiveRoughness.a;

    vec3 viewDir = normalize(fragPos - basicDataUBO.ViewPos);

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < shadowDataUBO.PointCount; i++)
    {
        PointShadow pointShadow = shadowDataUBO.PointShadows[i];
        Light light = lightsUBO.Lights[i];
        vec3 sampleToLight = light.Position - fragPos;
        directLighting += GetBlinnPhongLighting(light, viewDir, normal, albedo, specular, roughness, sampleToLight) * Visibility(pointShadow, normal, -sampleToLight);
    }

    for (int i = shadowDataUBO.PointCount; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];
        vec3 sampleToLight = light.Position - fragPos;
        directLighting += GetBlinnPhongLighting(light, viewDir, normal, albedo, specular, roughness, sampleToLight);
    }
    vec3 indirectLight = vec3(0.0);
    if (IsVXGI)
    {
        indirectLight = IndirectLight(fragPos, normal, reflect(viewDir, normal)) * GIBoost;
    }
    else
    {
        indirectLight = vec3(0.03);
    }

    vec3 finalColor = (directLighting + indirectLight * albedo) * ambientOcclusion + emissive;

    imageStore(ImgResult, imgCoord, vec4(finalColor, albedoAlpha.a));
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

vec3 GetBlinnPhongLighting(Light light, vec3 viewDir, vec3 normal, vec3 albedo, float specular, float roughness, vec3 sampleToLight)
{
    float fragToLightLength = length(sampleToLight);

    vec3 lightDir = sampleToLight / fragToLightLength;
    float cosTerm = dot(normal, lightDir);
    if (cosTerm > 0.0)
    {
        vec3 diffuseContrib = light.Color * cosTerm * albedo;  
    
        vec3 specularContrib = vec3(0.0);
        vec3 halfwayDir = normalize(lightDir + -viewDir);
        float temp = dot(normal, halfwayDir);
        if (temp > 0.0)
        {
            float spec = pow(temp, 256.0 * (1.0 - roughness));
            specularContrib = light.Color * spec * specular;
        }
        
        vec3 attenuation = light.Color / (4.0 * PI * fragToLightLength * fragToLightLength);

        return (diffuseContrib + specularContrib) * attenuation;
    }
    return vec3(0.0);
}

// Source: https://learnopengl.com/Advanced-Lighting/Shadows/Point-Shadows
const vec3 SHADOW_SAMPLE_OFFSETS[] =
{
   vec3( 1.0,  1.0,  1.0 ), vec3(  1.0, -1.0,  1.0 ), vec3( -1.0, -1.0,  1.0 ), vec3( -1.0,  1.0,  1.0 ), 
   vec3( 1.0,  1.0, -1.0 ), vec3(  1.0, -1.0, -1.0 ), vec3( -1.0, -1.0, -1.0 ), vec3( -1.0,  1.0, -1.0 ),
   vec3( 1.0,  1.0,  0.0 ), vec3(  1.0, -1.0,  0.0 ), vec3( -1.0, -1.0,  0.0 ), vec3( -1.0,  1.0,  0.0 ),
   vec3( 1.0,  0.0,  1.0 ), vec3( -1.0,  0.0,  1.0 ), vec3(  1.0,  0.0, -1.0 ), vec3( -1.0,  0.0, -1.0 ),
   vec3( 0.0,  1.0,  1.0 ), vec3(  0.0, -1.0,  1.0 ), vec3(  0.0, -1.0, -1.0 ), vec3(  0.0,  1.0, -1.0 )
};

float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightToSample)
{
    float lightToFragLength = length(lightToSample);

    float twoDist = lightToFragLength * lightToFragLength;
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;
    
    const float MIN_BIAS = EPSILON;
    const float MAX_BIAS = 1.5;
    float twoBias = mix(MAX_BIAS * MAX_BIAS, MIN_BIAS * MIN_BIAS, max(dot(normal, lightToSample / lightToFragLength), 0.0));

    // Map from [nearPlane, farPlane] to [0.0, 1.0]
    float mapedDepth = (twoDist - twoBias - twoNearPlane) / (twoFarPlane - twoNearPlane);
    
    const float DISK_RADIUS = 0.08;
    float shadowFactor = texture(pointShadow.ShadowTexture, vec4(lightToSample, mapedDepth));
    for (int i = 0; i < SHADOW_SAMPLE_OFFSETS.length(); i++)
    {
        shadowFactor += texture(pointShadow.ShadowTexture, vec4(lightToSample + SHADOW_SAMPLE_OFFSETS[i] * DISK_RADIUS, mapedDepth));
    }

    return shadowFactor / 21.0;
}

vec3 NDCToWorldSpace(vec3 ndc)
{
    vec4 worldPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);
    return worldPos.xyz / worldPos.w;
}