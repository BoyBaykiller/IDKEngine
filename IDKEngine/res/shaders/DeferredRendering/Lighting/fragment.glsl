#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Random.glsl)

layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D SamplerAO;
layout(binding = 1) uniform sampler2D SamplerIndirectLighting;

struct DrawElementsCmd
{
    uint Count;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;

    uint BlasRootNodeIndex;
};

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    float _pad0;
    vec3 Absorbance;
    uint CubemapShadowCullInfo;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

struct BlasNode
{
    vec3 Min;
    uint TriStartOrLeftChild;
    vec3 Max;
    uint TriCount;
};

struct BlasTriangle
{
    vec3 Position0;
    uint VertexIndex0;

    vec3 Position1;
    uint VertexIndex1;

    vec3 Position2;
    uint VertexIndex2;
};

struct TlasNode
{
    vec3 Min;
    uint LeftChild;
    vec3 Max;
    uint BlasIndex;
};

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

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

layout(std430, binding = 0) restrict readonly buffer DrawElementsCmdSSBO
{
    DrawElementsCmd DrawCommands[];
} drawElementsCmdSSBO;

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 5) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std430, binding = 6) restrict readonly buffer BlasTriangleSSBO
{
    BlasTriangle Triangles[];
} blasTriangleSSBO;

layout(std430, binding = 7) restrict readonly buffer TlasSSBO
{
    TlasNode Nodes[];
} tlasSSBO;

vec3 GetBlinnPhongLighting(Light light, vec3 viewDir, vec3 normal, vec3 albedo, float specular, float roughness, vec3 sampleToLight);
float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightSpacePos);
float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpacePos);

#define SHADOW_MODE_NONE 0
#define SHADOW_MODE_PCF_SHADOW_MAP 1 
#define SHADOW_MODE_RAY_TRACED 2 
uniform int ShadowMode;

uniform int RayTracingSamples;

uniform bool IsVXGI;

in InOutVars
{
    vec2 TexCoord;
} inData;

#define TRAVERSAL_STACK_DONT_USE_SHARED_MEM
AppInclude(PathTracing/include/BVHIntersect.glsl)

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);
    vec2 uv = inData.TexCoord;
    
    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 1.0)
    {
        FragColor = vec4(0.0);
        return;
    }
    
    // InitializeRandomSeed((imgCoord.y * 4096 + imgCoord.x) * (basicDataUBO.Frame + 1));

    vec3 ndc = vec3(uv * 2.0 - 1.0, depth);
    vec3 fragPos = PerspectiveTransform(ndc, basicDataUBO.InvProjView);
    vec3 unjitteredFragPos = PerspectiveTransform(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), basicDataUBO.InvProjView);

    vec3 albedo = texelFetch(gBufferDataUBO.AlbedoAlpha, imgCoord, 0).rgb;
    float alpha = texelFetch(gBufferDataUBO.AlbedoAlpha, imgCoord, 0).a;
    vec3 normal = texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).rgb;
    float specular = texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).a;
    vec3 emissive = texelFetch(gBufferDataUBO.EmissiveRoughness, imgCoord, 0).rgb;
    float roughness = texelFetch(gBufferDataUBO.EmissiveRoughness, imgCoord, 0).a;

    vec3 viewDir = normalize(fragPos - basicDataUBO.ViewPos);

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];

        vec3 sampleToLight = light.Position - fragPos;
        vec3 contribution = GetBlinnPhongLighting(light, viewDir, normal, albedo, specular, roughness, sampleToLight);
        
        if (contribution != vec3(0.0))
        {
            if (ShadowMode == SHADOW_MODE_PCF_SHADOW_MAP)
            {
                if (light.PointShadowIndex >= 0)
                {
                    PointShadow pointShadow = shadowDataUBO.PointShadows[light.PointShadowIndex];
                    vec3 lightSpacePos = unjitteredFragPos - light.Position;
                    contribution *= Visibility(pointShadow, normal, lightSpacePos);
                }
            }
            else if (ShadowMode == SHADOW_MODE_RAY_TRACED)
            {
                float shadow = 0.0;
                uint noiseIndex = basicDataUBO.Frame * RayTracingSamples;
                for (int i = 0; i < RayTracingSamples; i++)
                {
                    vec3 offsetedPos = unjitteredFragPos + normal * 0.05;

                    vec3 lightSamplePoint;
                    {
                        float rnd0 = InterleavedGradientNoise(imgCoord, noiseIndex + 0);
                        float rnd1 = InterleavedGradientNoise(imgCoord, noiseIndex + 1);
                        noiseIndex++;

                        vec3 samplingDiskNormal = normalize(offsetedPos - light.Position);
                        lightSamplePoint = light.Position + UniformSampleHemisphere(samplingDiskNormal, rnd0, rnd1) * light.Radius;
                    }

                    float dist = distance(lightSamplePoint, offsetedPos);

                    Ray ray;
                    ray.Origin = offsetedPos;
                    ray.Direction = (lightSamplePoint - offsetedPos) / dist;

                    HitInfo hitInfo;
                    if (BVHRayTraceAny(ray, hitInfo, false, dist - 0.001))
                    {
                        shadow += 1.0;
                    }
                }
                shadow /= RayTracingSamples;

                contribution *= (1.0 - shadow);
            }
        }

        directLighting += contribution;
    }

    vec3 indirectLight;
    if (IsVXGI)
    {
        indirectLight = texelFetch(SamplerIndirectLighting, imgCoord, 0).rgb * albedo;
    }
    else
    {
        indirectLight = vec3(0.03) * albedo;
    }
    float ambientOcclusion = 1.0 - texelFetch(SamplerAO, imgCoord, 0).r;

    FragColor = vec4((directLighting + indirectLight) * ambientOcclusion + emissive, alpha);
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
        // TODO: Implement not shit lighting that doesnt break under some conditions
        if (!IsVXGI && temp > 0.0)
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
const vec3 ShadowSampleOffsets[] =
{
    vec3( 0.0,  0.0,  0.0 ),
    vec3( 1.0,  1.0,  1.0 ), vec3(  1.0, -1.0,  1.0 ), vec3( -1.0, -1.0,  1.0 ), vec3( -1.0,  1.0,  1.0 ), 
    vec3( 1.0,  1.0, -1.0 ), vec3(  1.0, -1.0, -1.0 ), vec3( -1.0, -1.0, -1.0 ), vec3( -1.0,  1.0, -1.0 ),
    vec3( 1.0,  1.0,  0.0 ), vec3(  1.0, -1.0,  0.0 ), vec3( -1.0, -1.0,  0.0 ), vec3( -1.0,  1.0,  0.0 ),
    vec3( 1.0,  0.0,  1.0 ), vec3( -1.0,  0.0,  1.0 ), vec3(  1.0,  0.0, -1.0 ), vec3( -1.0,  0.0, -1.0 ),
    vec3( 0.0,  1.0,  1.0 ), vec3(  0.0, -1.0,  1.0 ), vec3(  0.0, -1.0, -1.0 ), vec3(  0.0,  1.0, -1.0 )
};

float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightSpacePos)
{
    float bias = 0.018;

    float visibilityFactor = 0.0;
    const float sampleDiskRadius = 0.04;
    for (int i = 0; i < ShadowSampleOffsets.length(); i++)
    {
        vec3 samplePos = (lightSpacePos + ShadowSampleOffsets[i] * sampleDiskRadius);
        float depth = GetLightSpaceDepth(pointShadow, samplePos * (1.0 - bias));
        visibilityFactor += texture(pointShadow.ShadowTexture, vec4(samplePos, depth));
    }
    visibilityFactor /= ShadowSampleOffsets.length();

    return visibilityFactor;
}

float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpacePos)
{
    float dist = max(abs(lightSpacePos.x), max(abs(lightSpacePos.y), abs(lightSpacePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}