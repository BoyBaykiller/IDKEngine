#version 460 core
#extension GL_ARB_bindless_texture : require
#extension GL_AMD_gpu_shader_half_float : enable
#extension GL_AMD_gpu_shader_half_float_fetch : enable // requires GL_AMD_gpu_shader_half_float

#if GL_AMD_gpu_shader_half_float_fetch
#define HF_SAMPLER_2D f16sampler2D
#else
#define HF_SAMPLER_2D sampler2D
#endif

AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Ray.glsl)
AppInclude(PathTracing/include/Constants.glsl)

layout(local_size_x = N_HIT_PROGRAM_LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

struct Material
{
    vec3 EmissiveFactor;
    uint BaseColorFactor;

    float TransmissionFactor;
    float AlphaCutoff;
    float RoughnessFactor;
    float MetallicFactor;

    vec3 Absorbance;
    float IOR;

    HF_SAMPLER_2D Transmission;
    uvec2 _pad0;

    HF_SAMPLER_2D BaseColor;
    HF_SAMPLER_2D MetallicRoughness;

    HF_SAMPLER_2D Normal;
    HF_SAMPLER_2D Emissive;
};

struct DrawElementsCmd
{
    uint IndexCount;
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
    float TransmissionBias;
    float IORBias;
    uint MeshletsStart;
    vec3 AbsorbanceBias;
    uint MeshletCount;
};

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
};

struct Vertex
{
    vec2 TexCoord;
    uint Tangent;
    uint Normal;
};

struct BlasNode
{
    vec3 Min;
    uint TriStartOrLeftChild;
    vec3 Max;
    uint TriCount;
};

struct TlasNode
{
    vec3 Min;
    uint LeftChild;
    vec3 Max;
    uint BlasIndex;
};

struct WavefrontRay
{
    vec3 Origin;
    float PreviousIOROrDebugNodeCounter;

    vec3 Throughput;
    float CompressedDirectionX;

    vec3 Radiance;
    float CompressedDirectionY;
};

struct DispatchCommand
{
    uint NumGroupsX;
    uint NumGroupsY;
    uint NumGroupsZ;
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

layout(std430, binding = 3) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

layout(std430, binding = 4) restrict readonly buffer VertexSSBO
{
    Vertex Vertices[];
} vertexSSBO;

layout(std430, binding = 5) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std430, binding = 7) restrict readonly buffer TlasSSBO
{
    TlasNode Nodes[];
} tlasSSBO;

layout(std430, binding = 8) restrict buffer WavefrontRaySSBO
{
    WavefrontRay Rays[];
} wavefrontRaySSBO;

layout(std430, binding = 9) restrict buffer WavefrontPTSSBO
{
    DispatchCommand DispatchCommands[2];
    uint Counts[2];
    uint AccumulatedSamples;
    uint Indices[];
} wavefrontPTSSBO;

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

layout(std140, binding = 2) uniform LightsUBO
{
    Light Lights[GPU_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

bool TraceRay(inout WavefrontRay wavefrontRay);
vec3 BounceOffMaterial(vec3 incomming, float specularChance, float roughness, float transmissionChance, float ior, float prevIor, vec3 normal, bool fromInside, out float rayProbability, out float newIor, out bool isRefractive);
float FresnelSchlick(float cosTheta, float n1, float n2);

layout(location = 0) uniform int PingPongIndex;
uniform bool IsTraceLights;
uniform bool IsOnRefractionTintAlbedo;

AppInclude(PathTracing/include/BVHIntersect.glsl)
AppInclude(PathTracing/include/RussianRoulette.glsl)

void main()
{
    if (gl_GlobalInvocationID.x > wavefrontPTSSBO.Counts[1 - PingPongIndex])
    {
        return;
    }

    if (gl_GlobalInvocationID.x == 0)
    {
        wavefrontPTSSBO.DispatchCommands[1 - PingPongIndex].NumGroupsX = 0u;
    }

    InitializeRandomSeed(gl_GlobalInvocationID.x * 4096 + wavefrontPTSSBO.AccumulatedSamples);

    uint rayIndex = wavefrontPTSSBO.Indices[gl_GlobalInvocationID.x];
    WavefrontRay wavefrontRay = wavefrontRaySSBO.Rays[rayIndex];
    
    bool continueRay = TraceRay(wavefrontRay);
    wavefrontRaySSBO.Rays[rayIndex] = wavefrontRay;

    if (continueRay)
    {
        uint index = atomicAdd(wavefrontPTSSBO.Counts[PingPongIndex], 1u);
        wavefrontPTSSBO.Indices[index] = rayIndex;

        if (index % N_HIT_PROGRAM_LOCAL_SIZE_X == 0)
        {
            atomicAdd(wavefrontPTSSBO.DispatchCommands[PingPongIndex].NumGroupsX, 1u);
        }
    }
}

bool TraceRay(inout WavefrontRay wavefrontRay)
{
    vec3 uncompressedDir = DecompressOctahedron(vec2(wavefrontRay.CompressedDirectionX, wavefrontRay.CompressedDirectionY));

    HitInfo hitInfo;
    if (BVHRayTrace(Ray(wavefrontRay.Origin, uncompressedDir), hitInfo, IsTraceLights, FLOAT_MAX))
    {
        wavefrontRay.Origin += uncompressedDir * hitInfo.T;

        vec3 albedo = vec3(0.0);
        vec3 normal = vec3(0.0);
        vec3 emissive = vec3(0.0);
        float transmissionChance = 0.0;
        float specularChance = 0.0;
        float roughness = 0.0;
        float ior = 1.0;
        vec3 absorbance = vec3(1.0);
        
        bool hitLight = hitInfo.VertexIndices == uvec3(0);
        if (!hitLight)
        {
            Vertex v0 = vertexSSBO.Vertices[hitInfo.VertexIndices.x];
            Vertex v1 = vertexSSBO.Vertices[hitInfo.VertexIndices.y];
            Vertex v2 = vertexSSBO.Vertices[hitInfo.VertexIndices.z];

            vec2 interpTexCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);
            vec3 interpNormal = normalize(Interpolate(DecompressSR11G11B10(v0.Normal), DecompressSR11G11B10(v1.Normal), DecompressSR11G11B10(v2.Normal), hitInfo.Bary));
            vec3 interpTangent = normalize(Interpolate(DecompressSR11G11B10(v0.Tangent), DecompressSR11G11B10(v1.Tangent), DecompressSR11G11B10(v2.Tangent), hitInfo.Bary));

            Mesh mesh = meshSSBO.Meshes[hitInfo.MeshID];
            Material material = materialSSBO.Materials[mesh.MaterialIndex];
            MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[hitInfo.InstanceID];

            mat3 unitVecToWorld = mat3(transpose(meshInstance.InvModelMatrix));
            vec3 worldNormal = normalize(unitVecToWorld * interpNormal);
            vec3 worldTangent = normalize(unitVecToWorld * interpTangent);
            mat3 TBN = GetTBN(worldTangent, worldNormal);
            
            normal = texture(material.Normal, interpTexCoord).rgb;
            normal = TBN * normalize(normal * 2.0 - 1.0);
            normal = mix(worldNormal, normal, mesh.NormalMapStrength);

            ior = max(material.IOR + mesh.IORBias, 1.0);
            vec4 albedoAlpha = texture(material.BaseColor, interpTexCoord) * DecompressUR8G8B8A8(material.BaseColorFactor);
            albedo = albedoAlpha.rgb;
            absorbance = max(mesh.AbsorbanceBias + material.Absorbance, vec3(0.0));
            emissive = texture(material.Emissive, interpTexCoord).rgb * material.EmissiveFactor * MATERIAL_EMISSIVE_FACTOR + mesh.EmissiveBias * albedo;
            
            transmissionChance = clamp(texture(material.Transmission, interpTexCoord).r * material.TransmissionFactor + mesh.TransmissionBias, 0.0, 1.0);
            roughness = clamp(texture(material.MetallicRoughness, interpTexCoord).g * material.RoughnessFactor + mesh.RoughnessBias, 0.0, 1.0);
            specularChance = clamp(texture(material.MetallicRoughness, interpTexCoord).r * material.MetallicFactor + mesh.SpecularBias, 0.0, 1.0 - transmissionChance);
            if (albedoAlpha.a < material.AlphaCutoff)
            {
                wavefrontRay.Origin += uncompressedDir * 0.001;
                return true;
            }
        }
        else if (IsTraceLights)
        {
            Light light = lightsUBO.Lights[hitInfo.MeshID];
            emissive = light.Color;
            albedo = light.Color;
            normal = (wavefrontRay.Origin - light.Position) / light.Radius;
        }

        float cosTheta = dot(-uncompressedDir, normal);
        bool fromInside = cosTheta < 0.0;

        if (fromInside)
        {
            normal *= -1.0;
            cosTheta *= -1.0;

            wavefrontRay.Throughput *= exp(-absorbance * hitInfo.T);
        }

        if (specularChance > 0.0) // adjust specular chance based on view angle
        {
            float newSpecularChance = mix(specularChance, 1.0, FresnelSchlick(cosTheta, wavefrontRay.PreviousIOROrDebugNodeCounter, ior));
            float chanceMultiplier = (1.0 - newSpecularChance) / (1.0 - specularChance);
            transmissionChance *= chanceMultiplier;
            specularChance = newSpecularChance;
        }
        
        wavefrontRay.Radiance += emissive * wavefrontRay.Throughput;

        float rayProbability, newIor;
        bool newRayRefractive;
        uncompressedDir = BounceOffMaterial(uncompressedDir, specularChance, roughness, transmissionChance, ior, wavefrontRay.PreviousIOROrDebugNodeCounter, normal, fromInside, rayProbability, newIor, newRayRefractive);
        wavefrontRay.Origin += uncompressedDir * EPSILON;
        wavefrontRay.PreviousIOROrDebugNodeCounter = newIor;

        vec2 compressedDir = CompressOctahedron(uncompressedDir);
        wavefrontRay.CompressedDirectionX = compressedDir.x;
        wavefrontRay.CompressedDirectionY = compressedDir.y;

        if (!newRayRefractive || IsOnRefractionTintAlbedo)
        {
            wavefrontRay.Throughput *= albedo;
        }
        wavefrontRay.Throughput /= rayProbability;

        bool terminateRay = RussianRouletteTerminateRay(wavefrontRay.Throughput);
        return !terminateRay;
    }
    else
    {
        wavefrontRay.Radiance += texture(skyBoxUBO.Albedo, uncompressedDir).rgb * wavefrontRay.Throughput;
        return false;
    }
}

vec3 BounceOffMaterial(vec3 incomming, float specularChance, float roughness, float transmissionChance, float ior, float prevIor, vec3 normal, bool fromInside, out float rayProbability, out float newIor, out bool isRefractive)
{
    isRefractive = false;
    roughness *= roughness;

    float rnd = GetRandomFloat01();
    vec3 diffuseRayDir = CosineSampleHemisphere(normal);
    vec3 outgoing;
    if (specularChance > rnd)
    {
        vec3 reflectionRayDir = reflect(incomming, normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRayDir, roughness));
        outgoing = reflectionRayDir;
        rayProbability = specularChance;
        newIor = prevIor;
    }
    else if (specularChance + transmissionChance > rnd)
    {
        if (fromInside)
        {
            // we don't actually know wheter the next mesh we hit has ior 1.0
            newIor = 1.0;
        }
        else
        {
            newIor = ior;
        }
        vec3 refractionRayDir = refract(incomming, normal, prevIor / newIor);
        isRefractive = refractionRayDir != vec3(0.0);
        if (!isRefractive) // Total Internal Reflection
        {
            refractionRayDir = reflect(incomming, normal);
            newIor = prevIor;
        }
        refractionRayDir = normalize(mix(refractionRayDir, isRefractive ? -diffuseRayDir : diffuseRayDir, roughness));
        outgoing = refractionRayDir;
        rayProbability = transmissionChance;
    }
    else
    {
        outgoing = diffuseRayDir;
        rayProbability = 1.0 - specularChance - transmissionChance;
        newIor = prevIor;
    }
    rayProbability = max(rayProbability, EPSILON);

    return outgoing;
}

float FresnelSchlick(float cosTheta, float n1, float n2)
{
    float r0 = (n1 - n2) / (n1 + n2);
    r0 *= r0;

    return r0 + (1.0 - r0) * pow(1.0 - cosTheta, 5.0);
}

