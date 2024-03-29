#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Pbr.glsl)

layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D SamplerAO;
layout(binding = 1) uniform sampler2D SamplerIndirectLighting;

struct HitInfo
{
    vec3 Bary;
    float T;
    uvec3 VertexIndices;
    uint InstanceID;
};

struct DrawElementsCmd
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float TransmissionBias;
    float IORBias;
    uint MeshletsStart;
    vec3 AbsorbanceBias;
    uint MeshletCount;
    uint InstanceCount;
    uint BlasRootNodeIndex;
    vec2 _pad0;
};

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
    vec3 _pad0;
    uint MeshIndex;
};

struct BlasNode
{
    vec3 Min;
    uint TriStartOrChild;
    vec3 Max;
    uint TriCount;
};

struct TlasNode
{
    vec3 Min;
    uint IsLeafAndChildOrInstanceID;
    vec3 Max;
    float _pad0;
};

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    int PointShadowIndex;
    vec3 PrevPosition;
    float _pad0;
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
    float DeltaRenderTime;
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
    int SampleCount;
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

layout(std430, binding = 2, row_major) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 5) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std430, binding = 7) restrict readonly buffer TlasSSBO
{
    TlasNode Nodes[];
} tlasSSBO;

vec3 GetBlinnPhongLighting(Light light, vec3 viewDir, vec3 normal, vec3 albedo, float specular, float roughness, vec3 sampleToLight, float ambientOcclusion);
float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightToSample);
float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpaceSamplePos);

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
    float ambientOcclusion = 1.0 - texelFetch(SamplerAO, imgCoord, 0).r;

    vec3 viewDir = normalize(fragPos - basicDataUBO.ViewPos);

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];

        vec3 sampleToLight = light.Position - fragPos;
        vec3 contribution = GetBlinnPhongLighting(light, viewDir, normal, albedo, specular, roughness, sampleToLight, ambientOcclusion);
        
        if (contribution != vec3(0.0))
        {
            if (ShadowMode == SHADOW_MODE_PCF_SHADOW_MAP)
            {
                if (light.PointShadowIndex >= 0)
                {
                    PointShadow pointShadow = shadowDataUBO.PointShadows[light.PointShadowIndex];
                    vec3 lightToSample = unjitteredFragPos - light.Position;
                    contribution *= Visibility(pointShadow, normal, lightToSample);
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
                    if (TraceRayAny(ray, hitInfo, false, dist - 0.001))
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
        const vec3 ambient = vec3(0.015);
        indirectLight = ambient * albedo;
    }

    FragColor = vec4((directLighting + indirectLight) + emissive, 1.0);
    // FragColor = vec4(albedo, 1.0);
}

vec3 GetBlinnPhongLighting(Light light, vec3 viewDir, vec3 normal, vec3 albedo, float specular, float roughness, vec3 sampleToLight, float ambientOcclusion)
{
    float dist = length(sampleToLight);

    vec3 lightDir = sampleToLight / dist;
    float cosTerm = dot(normal, lightDir);
    if (cosTerm > 0.0)
    {
        vec3 diffuseContrib = light.Color * cosTerm * albedo * ambientOcclusion;  
    
        // TODO: Implement not shit lighting that doesnt break under some conditions
        vec3 specularContrib = vec3(0.0);
        if (!IsVXGI)
        {
            vec3 halfwayDir = normalize(lightDir + -viewDir);
            float temp = dot(normal, halfwayDir);
            if (temp > 0.0)
            {
                // double spec = pow(double(temp), 256.0lf * (1.0lf - double(roughness)));
                // This bugged on bistro for some reason
                float spec = pow(temp, 256.0 * (1.0 - roughness));
                specularContrib = light.Color * float(spec) * specular;
            }
        }
        
        float attenuation = GetAttenuationFactor(dist * dist, light.Radius);

        return (diffuseContrib + specularContrib) * attenuation;
    }
    return vec3(0.0);
}

float Visibility(PointShadow pointShadow, vec3 normal, vec3 lightToSample)
{
    // TODO: Use overall better sampling method
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
    
    const float bias = 0.018;
    const float sampleDiskRadius = 0.04;

    float visibilityFactor = 0.0;
    for (int i = 0; i < ShadowSampleOffsets.length(); i++)
    {
        vec3 samplePos = (lightToSample + ShadowSampleOffsets[i] * sampleDiskRadius);
        float depth = GetLightSpaceDepth(pointShadow, samplePos * (1.0 - bias));
        visibilityFactor += texture(pointShadow.ShadowTexture, vec4(samplePos, depth));
    }
    visibilityFactor /= ShadowSampleOffsets.length();

    return visibilityFactor;
}

float GetLightSpaceDepth(PointShadow pointShadow, vec3 lightSpaceSamplePos)
{
    float dist = max(abs(lightSpaceSamplePos.x), max(abs(lightSpaceSamplePos.y), abs(lightSpaceSamplePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}