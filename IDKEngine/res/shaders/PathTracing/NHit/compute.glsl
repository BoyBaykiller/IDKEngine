#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX
#define EPSILON 0.001
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require
#extension GL_AMD_gpu_shader_half_float : enable
#extension GL_AMD_gpu_shader_half_float_fetch : enable // requires GL_AMD_gpu_shader_half_float

#if defined GL_AMD_gpu_shader_half_float_fetch
#define HF_SAMPLER_2D f16sampler2D
#else
#define HF_SAMPLER_2D sampler2D
#endif

AppInclude(include/Constants.glsl)
AppInclude(PathTracing/include/Constants.glsl)

layout(local_size_x = N_HIT_PROGRAM_LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

struct Ray
{
    vec3 Origin;
    vec3 Direction;
};

struct Material
{
    vec3 EmissiveFactor;
    uint BaseColorFactor;

    vec2 _pad0;
    float RoughnessFactor;
    float MetallicFactor;

    HF_SAMPLER_2D BaseColor;
    HF_SAMPLER_2D MetallicRoughness;

    HF_SAMPLER_2D Normal;
    HF_SAMPLER_2D Emissive;
};

struct DrawCommand
{
    uint Count;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct Mesh
{
    int InstanceCount;
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    vec3 Absorbance;
    uint CubemapShadowCullInfo;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

struct Vertex
{
    vec3 Position;
    float _pad0;

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

struct Triangle
{
    Vertex Vertex0;
    Vertex Vertex1;
    Vertex Vertex2;
};

struct TlasNode
{
    vec3 Min;
    uint LeftChildAndRightChild;
    vec3 Max;
    uint BlasIndex;
};

struct TransportRay
{
    vec3 Origin;
    uint DebugNodeCounter;

    vec3 Direction;
    float PreviousIOR;

    vec3 Throughput;
    bool IsRefractive;

    vec3 Radiance;
    float _pad0;
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
};

layout(std430, binding = 0) restrict readonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandSSBO;

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 3) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

layout(std430, binding = 4) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std430, binding = 5) restrict readonly buffer BlasTriangleSSBO
{
    Triangle Triangles[];
} blasTriangleSSBO;

layout(std430, binding = 6) restrict readonly buffer TlasSSBO
{
    TlasNode Nodes[];
} tlasSSBO;

layout(std430, binding = 7) restrict buffer TransportRaySSBO
{
    TransportRay Rays[];
} transportRaySSBO;

layout(std430, binding = 8) restrict buffer RayIndicesSSBO
{
    uint Counts[2];
    uint AccumulatedSamples;
    uint Indices[];
} rayIndicesSSBO;

layout(std430, binding = 9) restrict buffer DispatchCommandSSBO
{
    DispatchCommand DispatchCommands[2];
} dispatchCommandSSBO;

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

layout(std140, binding = 2) uniform LightsUBO
{
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

bool TraceRay(inout TransportRay transportRay);
vec3 BounceOffMaterial(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, float prevIor, vec3 normal, bool fromInside, out float rayProbability, out float newIor, out bool isRefractive);
float FresnelSchlick(float cosTheta, float n1, float n2);
Ray WorldSpaceRayToLocal(Ray ray, mat4 invModel);

layout(location = 0) uniform int PingPongIndex;
uniform bool IsTraceLights;

AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Random.glsl)
AppInclude(PathTracing/include/ClosestHit.glsl)

void main()
{
    if (gl_GlobalInvocationID.x > rayIndicesSSBO.Counts[1 - PingPongIndex])
        return;

    if (gl_GlobalInvocationID.x == 0)
    {
        dispatchCommandSSBO.DispatchCommands[1 - PingPongIndex].NumGroupsX = 0u;
    }

    InitializeRandomSeed(gl_GlobalInvocationID.x * 4096 + rayIndicesSSBO.AccumulatedSamples);

    uint rayIndex = rayIndicesSSBO.Indices[gl_GlobalInvocationID.x];
    TransportRay transportRay = transportRaySSBO.Rays[rayIndex];
    
    bool continueRay = TraceRay(transportRay);
    transportRaySSBO.Rays[rayIndex] = transportRay;

    if (continueRay)
    {
        uint index = atomicAdd(rayIndicesSSBO.Counts[PingPongIndex], 1u);
        rayIndicesSSBO.Indices[index] = rayIndex;

        if (index % N_HIT_PROGRAM_LOCAL_SIZE_X == 0)
        {
            atomicAdd(dispatchCommandSSBO.DispatchCommands[PingPongIndex].NumGroupsX, 1u);
        }
    }
}

bool TraceRay(inout TransportRay transportRay)
{
    HitInfo hitInfo;
    if (ClosestHit(Ray(transportRay.Origin, transportRay.Direction), hitInfo))
    {
        transportRay.Origin += transportRay.Direction * hitInfo.T;

        vec3 albedo;
        vec3 normal;
        vec3 emissive;
        float refractionChance;
        float specularChance;
        float roughness;
        float ior;
        vec3 absorbance;

        bool hitLight = hitInfo.TriangleIndex == -1;
        if (!hitLight)
        {
            Triangle triangle = blasTriangleSSBO.Triangles[hitInfo.TriangleIndex];
            Vertex v0 = triangle.Vertex0;
            Vertex v1 = triangle.Vertex1;
            Vertex v2 = triangle.Vertex2;

            vec2 texCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);
            vec3 geoNormal = normalize(Interpolate(DecompressSNorm32Fast(v0.Normal), DecompressSNorm32Fast(v1.Normal), DecompressSNorm32Fast(v2.Normal), hitInfo.Bary));
            vec3 tangent = normalize(Interpolate(DecompressSNorm32Fast(v0.Tangent), DecompressSNorm32Fast(v1.Tangent), DecompressSNorm32Fast(v2.Tangent), hitInfo.Bary));

            MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[hitInfo.InstanceID];
            vec3 T = normalize((meshInstance.ModelMatrix * vec4(tangent, 0.0)).xyz);
            vec3 N = normalize((meshInstance.ModelMatrix * vec4(geoNormal, 0.0)).xyz);
            T = normalize(T - dot(T, N) * N);
            vec3 B = cross(N, T);
            mat3 TBN = mat3(T, B, N);

            Mesh mesh = meshSSBO.Meshes[hitInfo.MeshID];
            Material material = materialSSBO.Materials[mesh.MaterialIndex];

            vec4 albedoAlpha = texture(material.BaseColor, texCoord) * unpackUnorm4x8(material.BaseColorFactor);
            albedo = albedoAlpha.rgb;
            refractionChance = clamp((1.0 - albedoAlpha.a) + mesh.RefractionChance, 0.0, 1.0);
            emissive = (texture(material.Emissive, texCoord).rgb * material.EmissiveFactor) + mesh.EmissiveBias * albedo;
            specularChance = clamp(texture(material.MetallicRoughness, texCoord).r * material.MetallicFactor + mesh.SpecularBias, 0.0, 1.0 - refractionChance);
            roughness = clamp(texture(material.MetallicRoughness, texCoord).g * material.RoughnessFactor + mesh.RoughnessBias, 0.0, 1.0);
            normal = texture(material.Normal, texCoord).rgb;
            normal = TBN * normalize(normal * 2.0 - 1.0);
            mat3 normalToWorld = mat3(transpose(meshInstance.InvModelMatrix));
            normal = normalize(mix(normalize(normalToWorld * geoNormal), normal, mesh.NormalMapStrength));
            ior = mesh.IOR;
            absorbance = mesh.Absorbance;
        }
        else
        {
            Light light = lightsUBO.Lights[hitInfo.MeshID];
            emissive = light.Color;
            albedo = light.Color;
            normal = (transportRay.Origin - light.Position) / light.Radius;

            refractionChance = 0.0;
            specularChance = 1.0;
            roughness = 0.0;
            ior = 1.0;
            absorbance = vec3(0.0);
        }

        float cosTheta = dot(-transportRay.Direction, normal);
        bool fromInside = cosTheta < 0.0;
        if (fromInside)
        {
            normal *= -1.0;
            cosTheta *= -1.0;
        }

        if (specularChance > 0.0) // adjust specular chance based on view angle
        {
            float newSpecularChance = mix(specularChance, 1.0, FresnelSchlick(cosTheta, transportRay.PreviousIOR, ior));
            float chanceMultiplier = (1.0 - newSpecularChance) / (1.0 - specularChance);
            refractionChance *= chanceMultiplier;
            specularChance = newSpecularChance;
        }

        float rayProbability, newIor;
        transportRay.Direction = BounceOffMaterial(transportRay.Direction, specularChance, roughness, refractionChance, ior, transportRay.PreviousIOR, normal, fromInside, rayProbability, newIor, transportRay.IsRefractive);
        transportRay.Origin += transportRay.Direction * EPSILON;
        transportRay.PreviousIOR = newIor;

        if (fromInside)
        {
            transportRay.Throughput *= exp(-absorbance * hitInfo.T);
        }

        transportRay.Radiance += emissive * transportRay.Throughput;
        if (!transportRay.IsRefractive)
        {
            transportRay.Throughput *= albedo;
        }
        transportRay.Throughput /= rayProbability;

        float p = max(transportRay.Throughput.x, max(transportRay.Throughput.y, transportRay.Throughput.z));
        if (GetRandomFloat01() > p)
        {
            return false;
        }
        transportRay.Throughput /= p;

        return true;
    }
    else
    {
        transportRay.Radiance += texture(skyBoxUBO.Albedo, transportRay.Direction).rgb * transportRay.Throughput;
        return false;
    }
}

vec3 BounceOffMaterial(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, float prevIor, vec3 normal, bool fromInside, out float rayProbability, out float newIor, out bool isRefractive)
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
    else if (specularChance + refractionChance > rnd)
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
        rayProbability = refractionChance;
    }
    else
    {
        outgoing = diffuseRayDir;
        rayProbability = 1.0 - specularChance - refractionChance;
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

Ray WorldSpaceRayToLocal(Ray ray, mat4 invModel)
{
    return Ray((invModel * vec4(ray.Origin, 1.0)).xyz, (invModel * vec4(ray.Direction, 0.0)).xyz);
}
