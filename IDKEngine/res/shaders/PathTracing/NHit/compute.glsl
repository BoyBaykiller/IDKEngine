#version 460 core
#define EMISSIVE_MATERIAL_MULTIPLIER 5.0
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -3.4028235e+38
#define EPSILON 0.001
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require
#extension GL_AMD_shader_trinary_minmax : enable
#extension GL_NV_compute_shader_derivatives : enable
#extension GL_NV_gpu_shader5 : enable
#ifndef GL_NV_gpu_shader5
#extension GL_ARB_shader_ballot : require
#endif

#ifdef GL_NV_compute_shader_derivatives
layout(derivative_group_quadsNV) in;
#endif

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(binding = 0) uniform samplerCube SamplerSkyBox;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    float _pad0;
};

struct Material
{
    sampler2D Albedo;
    sampler2D Normal;
    sampler2D Roughness;
    sampler2D Specular;
    sampler2D Emissive;
};

struct DrawCommand
{
    int Count;
    int InstanceCount;
    int FirstIndex;
    int BaseVertex;
    int BaseInstance;
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
    int VisibleCubemapFacesInfo;
};

struct Vertex
{
    vec3 Position;
    float _pad0;

    vec2 TexCoord;
    uint Tangent;
    uint Normal;
};

struct HitInfo
{
    vec3 Bary;
    float T;
    uint TriangleIndex;
    int HitIndex;
    int InstanceID;
};

struct Ray
{
    vec3 Origin;
    vec3 Direction;
};

struct Node
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

struct TransportRay
{
    vec3 Origin;
    uint IsRefractive;

    vec3 Direction;
    float CurrentIOR;

    vec3 Throughput;
    uint DebugFirstHitInteriorNodeCounter;

    vec3 Radiance;
    float _pad1;
};

struct DispatchCommand
{
    uint NumGroupsX;
    uint NumGroupsY;
    uint NumGroupsZ;
};

layout(std430, binding = 0) restrict readonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandSSBO;

layout(std430, binding = 1) restrict readonly buffer BlasSSBO
{
    Node Nodes[];
} blasSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 3) restrict readonly buffer TriangleSSBO
{
    Triangle Triangles[];
} triangleSSBO;

layout(std430, binding = 4) restrict readonly buffer MatrixSSBO
{
    mat4 Models[];
} matrixSSBO;

layout(std430, binding = 5) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

layout(std430, binding = 6) restrict writeonly buffer TransportRaySSBO
{
    TransportRay Rays[];
} transportRaySSBO;

layout(std430, binding = 7) restrict buffer RayIndicesSSBO
{
    uint Length;
    uint Indices[];
} rayIndicesSSBO;

layout(std430, binding = 8) restrict writeonly buffer DispatchCommandSSBO
{
    DispatchCommand DispatchCommand;
} dispatchCommandSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FreezeFrameCounter;
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
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(binding = 0, offset = 0) uniform atomic_uint AliveRaysCounter;

bool TraceRay(inout TransportRay transportRay);
vec3 BSDF(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, float prevIor, vec3 normal, out float rayProbability, out bool isRefractive);
float FresnelSchlick(float cosTheta, float n1, float n2);
bool ClosestHit(Ray ray, out HitInfo hitInfo);
bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec4 baryT);
bool RayCuboidIntersect(Ray ray, Node node, out float t1, out float t2);
bool RaySphereIntersect(Ray ray, Light light, out float t1, out float t2);
vec3 Interpolate(vec3 v0, vec3 v1, vec3 v2, vec3 bary);
vec2 Interpolate(vec2 v0, vec2 v1, vec2 v2, vec3 bary);
Ray WorldSpaceRayToLocal(Ray ray, mat4 invModel);
vec3 UniformSampleSphere();
vec3 CosineSampleHemisphere(vec3 normal);
vec2 UniformSampleCircle();
uint GetPCGHash(inout uint seed);
float GetRandomFloat01();
vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords);
uint EmulateNonUniform(uint index);
vec3 UnpackR11G11B10(uint v);

uniform bool IsRNGFrameBased;
layout(location = 10) uniform int Debug;

uint rngSeed;

void main()
{
    if (IsRNGFrameBased)
    {
        rngSeed = basicDataUBO.FreezeFrameCounter * 2699;
    }
    else
    {
        rngSeed = gl_GlobalInvocationID.x * 312 + basicDataUBO.FreezeFrameCounter * 2699;
    }

    uint rayIndex = rayIndicesSSBO.Indices[gl_GlobalInvocationID.x];
    TransportRay transportRay = transportRaySSBO.Rays[rayIndex];
    
    bool isRayTerminated = TraceRay(transportRay);
    transportRaySSBO.Rays[rayIndex] = transportRay;

    if (!isRayTerminated)
    {
        rayIndicesSSBO.Indices[atomicAdd(rayIndicesSSBO.Length, 1u)] = rayIndex;
    }
    else
    {
        uint aliveRayCount = atomicCounterDecrement(AliveRaysCounter);

        uint numWorkGroupsX = (aliveRayCount + gl_WorkGroupSize.x - 1) / gl_WorkGroupSize.x;
        atomicMin(dispatchCommandSSBO.DispatchCommand.NumGroupsX, numWorkGroupsX);
    }
}

bool TraceRay(inout TransportRay transportRay)
{
    HitInfo hitInfo;
    if (ClosestHit(Ray(transportRay.Origin, transportRay.Direction), hitInfo))
    {
        // Render wireframe
        // return float(any(lessThan(hitInfo.Bary, vec3(0.01)))).xxx;

        vec3 hitpos = transportRay.Origin + transportRay.Direction * hitInfo.T;
        float specularChance = 0.0;
        float refractionChance = 0.0;
        float roughness = 1.0;
        float alpha = 1.0;
        float ior = 1.0;
        vec3 absorbance = vec3(0.0);
        vec3 albedo;
        vec3 normal;
        vec3 emissive;
        if (hitInfo.HitIndex >= 0)
        {
            Triangle triangle = triangleSSBO.Triangles[hitInfo.TriangleIndex];
            Vertex v0 = triangle.Vertex0;
            Vertex v1 = triangle.Vertex1;
            Vertex v2 = triangle.Vertex2;

            mat4 model = matrixSSBO.Models[hitInfo.InstanceID];

            vec2 texCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);
            vec3 geoNormal = normalize(Interpolate(UnpackR11G11B10(v0.Normal) * 2.0 - 1.0, UnpackR11G11B10(v1.Normal) * 2.0 - 1.0, UnpackR11G11B10(v2.Normal) * 2.0 - 1.0, hitInfo.Bary));
            vec3 tangent = normalize(Interpolate(UnpackR11G11B10(v0.Tangent) * 2.0 - 1.0, UnpackR11G11B10(v1.Tangent) * 2.0 - 1.0, UnpackR11G11B10(v2.Tangent) * 2.0 - 1.0, hitInfo.Bary));

            vec3 T = normalize(vec3(model * vec4(tangent, 0.0)));
            vec3 N = normalize(vec3(model * vec4(geoNormal, 0.0)));
            T = normalize(T - dot(T, N) * N);
            vec3 B = cross(N, T);
            mat3 TBN = mat3(T, B, N);

            Mesh mesh = meshSSBO.Meshes[hitInfo.HitIndex];
        #ifdef GL_NV_gpu_shader5
            Material material = materialSSBO.Materials[mesh.MaterialIndex];
        #else
            Material material = materialSSBO.Materials[EmulateNonUniform(mesh.MaterialIndex)];
        #endif
            vec4 albedoAlpha = texture(material.Albedo, texCoord);
            albedo = albedoAlpha.rgb;
            refractionChance = clamp((1.0 - albedoAlpha.a) + mesh.RefractionChance, 0.0, 1.0);
            emissive = (texture(material.Emissive, texCoord).rgb * EMISSIVE_MATERIAL_MULTIPLIER + mesh.EmissiveBias) * albedo;
            specularChance = clamp(texture(material.Specular, texCoord).r + mesh.SpecularBias, 0.0, 1.0 - refractionChance);
            roughness = clamp(texture(material.Roughness, texCoord).r + mesh.RoughnessBias, 0.0, 1.0);
            normal = texture(material.Normal, texCoord).rgb;
            ior = mesh.IOR;
            absorbance = mesh.Absorbance;

            normal = TBN * normalize(normal * 2.0 - 1.0);
            normal = normalize(mix(geoNormal, normal, mesh.NormalMapStrength));
        }
        else
        {
            Light light = lightsUBO.Lights[-hitInfo.HitIndex - 1];
            emissive = light.Color;
            albedo = light.Color;
            normal = (hitpos - light.Position) / light.Radius;
        }

        bool isRefractive = bool(transportRay.IsRefractive);
        if (isRefractive)
        {
            transportRay.Throughput *= exp(-absorbance * hitInfo.T);
        }

        float rayProbability;
        transportRay.Direction = BSDF(transportRay.Direction, specularChance, roughness, refractionChance, ior, transportRay.CurrentIOR, normal, rayProbability, isRefractive);
        transportRay.Origin = hitpos + transportRay.Direction * EPSILON;
        transportRay.IsRefractive = uint(isRefractive);
        transportRay.CurrentIOR = ior;
        
        transportRay.Radiance += emissive * transportRay.Throughput;
        if (!isRefractive)
        {
            transportRay.Throughput *= albedo;
        }
        transportRay.Throughput /= rayProbability;

    #if GL_AMD_shader_trinary_minmax
        float p = max3(transportRay.Throughput.x, transportRay.Throughput.y, transportRay.Throughput.z);
    #else
        float p = max(transportRay.Throughput.x, max(transportRay.Throughput.y, transportRay.Throughput.z));
    #endif
        if (GetRandomFloat01() > p)
            return true;

        transportRay.Throughput /= p;

        return false;
    }
    else
    {
        transportRay.Radiance += texture(SamplerSkyBox, transportRay.Direction).rgb * transportRay.Throughput;
        return true;
    }
}

vec3 BSDF(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, float prevIor, vec3 normal, out float rayProbability, out bool isRefractive)
{
    float cosTheta = dot(-incomming, normal);
    bool fromInside = cosTheta < 0.0;
    if (fromInside)
        normal *= -1.0;

    isRefractive = false;
    if (specularChance > 0.0)
    {
        specularChance = mix(specularChance, 1.0, FresnelSchlick(cosTheta, fromInside ? ior : prevIor, fromInside ? prevIor : ior));
        float diffuseChance = 1.0 - specularChance - refractionChance;
        refractionChance = 1.0 - specularChance - diffuseChance;
    }

    vec3 diffuseRayDir = CosineSampleHemisphere(normal);
    float raySelectRoll = GetRandomFloat01();
    vec3 outgoing;
    if (specularChance > raySelectRoll)
    {
        vec3 reflectionRayDir = reflect(incomming, normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRayDir, roughness * roughness));
        outgoing = reflectionRayDir;
        rayProbability = specularChance;
    }
    else if (specularChance + refractionChance > raySelectRoll)
    {
        vec3 refractionRayDir = refract(incomming, normal, fromInside ? (ior / prevIor) : (prevIor / ior));
        refractionRayDir = normalize(mix(refractionRayDir, CosineSampleHemisphere(-normal), roughness * roughness));
        outgoing = refractionRayDir;
        rayProbability = refractionChance;
        isRefractive = true;
    }
    else
    {
        outgoing = diffuseRayDir;
        rayProbability = 1.0 - specularChance - refractionChance;
    }
    rayProbability = max(rayProbability, EPSILON);

    return outgoing;
}

float FresnelSchlick(float cosTheta, float n1, float n2)
{
    float r0 = (n1 - n2) / (n1 + n2);
    r0 *= r0;

    if (n1 > n2)
    {
        float n = n1 / n2;
        float sinT2 = n * n * (1.0 - cosTheta * cosTheta);
        // Total internal reflection
        if (sinT2 > 1.0)
            return 1.0;
        cosTheta = sqrt(1.0 - sinT2);
    }

    return r0 + (1.0 - r0) * pow(1.0 - cosTheta, 5.0);
}

bool ClosestHit(Ray ray, out HitInfo hitInfo)
{
    hitInfo.T = FLOAT_MAX;
    float rayTMin, rayTMax;

    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];
        if (RaySphereIntersect(ray, light, rayTMin, rayTMax) && rayTMax > 0.0 && rayTMin < hitInfo.T)
        {
            hitInfo.T = rayTMin;
            hitInfo.HitIndex = -i - 1;
        }
    }

    vec4 baryT;
    uint stack[32];
    for (int i = 0; i < meshSSBO.Meshes.length(); i++)
    {
        DrawCommand cmd = drawCommandSSBO.DrawCommands[i];
        int baseNode = 2 * (cmd.FirstIndex / 3);

        const int glInstanceID = 0; // TODO: Work out actual instanceID value
        Ray localRay = WorldSpaceRayToLocal(ray, inverse(matrixSSBO.Models[cmd.BaseInstance + glInstanceID]));

        uint stackPtr = 0;
        uint stackTop = 0;
        while (true)
        {
            Node node = blasSSBO.Nodes[baseNode + stackTop];
            if (!(RayCuboidIntersect(localRay, node, rayTMin, rayTMax) && rayTMax > 0.0 && rayTMin < hitInfo.T))
            {
                if (stackPtr == 0) break;
                stackTop = stack[--stackPtr];
                continue;
            }

            if (node.TriCount > 0)
            {
                for (uint j = node.TriStartOrLeftChild; j < node.TriStartOrLeftChild + node.TriCount; j++)
                {
                    Triangle triangle = triangleSSBO.Triangles[j];
                    if (RayTriangleIntersect(localRay, triangle.Vertex0.Position, triangle.Vertex1.Position, triangle.Vertex2.Position, baryT) && baryT.w > 0.0 && baryT.w < hitInfo.T)
                    {
                        hitInfo.Bary = baryT.xyz;
                        hitInfo.T = baryT.w;
                        hitInfo.HitIndex = i;
                        hitInfo.TriangleIndex = j;
                        hitInfo.InstanceID = cmd.BaseInstance + glInstanceID;
                    }
                }
            }
            else
            {
                float tMinLeft;
                float tMinRight;

                bool leftChildHit = RayCuboidIntersect(localRay, blasSSBO.Nodes[baseNode + node.TriStartOrLeftChild], tMinLeft, rayTMax) && rayTMax > 0.0 && tMinLeft < hitInfo.T;
                bool rightChildHit = RayCuboidIntersect(localRay, blasSSBO.Nodes[baseNode + node.TriStartOrLeftChild + 1], tMinRight, rayTMax) && rayTMax > 0.0 && tMinRight < hitInfo.T;

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        stackTop = node.TriStartOrLeftChild + (1 - int(tMinLeft < tMinRight));
                        stack[stackPtr++] = node.TriStartOrLeftChild + int(tMinLeft < tMinRight);
                    }
                    else
                    {
                        stackTop = node.TriStartOrLeftChild + int(rightChildHit && !leftChildHit);
                    }
                    continue;
                }
            }
            // Here: On a leaf node or didn't hit any children which means we should traverse up
            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
        }
    }

    return hitInfo.T != FLOAT_MAX;
}

// Source: https://www.iquilezles.org/www/articles/intersectors/intersectors.htm
bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec4 baryT)
{
    vec3 v1v0 = v1 - v0;
    vec3 v2v0 = v2 - v0;
    vec3 rov0 = ray.Origin - v0;
    vec3 normal = cross(v1v0, v2v0);
    vec3 q = cross(rov0, ray.Direction);

    // baryT = <u, v, w, t>

    baryT.xyw = vec3(dot(-q, v2v0), dot(q, v1v0), dot(-normal, rov0)) / dot(ray.Direction, normal);
    baryT.z = 1.0 - baryT.x - baryT.y;

    return all(greaterThanEqual(baryT.xyz, vec3(0.0)));
}

// Source: https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525
bool RayCuboidIntersect(Ray ray, Node node, out float t1, out float t2)
{
    t1 = FLOAT_MIN;
    t2 = FLOAT_MAX;

    vec3 t0s = (node.Min - ray.Origin) / ray.Direction;
    vec3 t1s = (node.Max - ray.Origin) / ray.Direction;

    vec3 tsmaller = min(t0s, t1s);
    vec3 tbigger = max(t0s, t1s);

    #if GL_AMD_shader_trinary_minmax
    t1 = max(t1, max3(tsmaller.x, tsmaller.y, tsmaller.z));
    t2 = min(t2, min3(tbigger.x, tbigger.y, tbigger.z));
    #else
    t1 = max(t1, max(tsmaller.x, max(tsmaller.y, tsmaller.z)));
    t2 = min(t2, min(tbigger.x, min(tbigger.y, tbigger.z)));
    #endif
    return t1 <= t2;
}

// Source: https://antongerdelan.net/opengl/raycasting.html
bool RaySphereIntersect(Ray ray, Light light, out float t1, out float t2)
{
    t1 = t2 = FLOAT_MAX;

    vec3 sphereToRay = ray.Origin - light.Position;
    float b = dot(ray.Direction, sphereToRay);
    float c = dot(sphereToRay, sphereToRay) - light.Radius * light.Radius;
    float discriminant = b * b - c;
    if (discriminant < 0.0)
        return false;

    float squareRoot = sqrt(discriminant);
    t1 = -b - squareRoot;
    t2 = -b + squareRoot;

    return t1 <= t2;
}

vec3 Interpolate(vec3 v0, vec3 v1, vec3 v2, vec3 bary)
{
    return v0 * bary.z + v1 * bary.x + v2 * bary.y;
}

vec2 Interpolate(vec2 v0, vec2 v1, vec2 v2, vec3 bary)
{
    return v0 * bary.z + v1 * bary.x + v2 * bary.y;
}

Ray WorldSpaceRayToLocal(Ray ray, mat4 invModel)
{
    return Ray((invModel * vec4(ray.Origin, 1.0)).xyz, (invModel * vec4(ray.Direction, 0.0)).xyz);
}

vec3 UniformSampleSphere()
{
    float z = GetRandomFloat01() * 2.0 - 1.0;
    float a = GetRandomFloat01() * 2.0 * PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);

    return vec3(x, y, z);
}

// Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
vec3 CosineSampleHemisphere(vec3 normal)
{
    // Convert unit vector in sphere to a cosine weighted vector in hemisphere
    return normalize(normal + UniformSampleSphere());
}

vec2 UniformSampleCircle()
{
    float angle = GetRandomFloat01() * 2.0 * PI;
    float r = sqrt(GetRandomFloat01());
    return vec2(cos(angle), sin(angle)) * r;
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

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords)
{
    vec4 rayEye = inverseProj * vec4(normalizedDeviceCoords, -1.0, 0.0);
    rayEye.zw = vec2(-1.0, 0.0);
    return normalize((inverseView * rayEye).xyz);
}

#ifndef GL_NV_gpu_shader5
// Source: https://discord.com/channels/318590007881236480/318590007881236480/856523979383373835
uint EmulateNonUniform(uint index)
{
    for (;;)
    {
        uint currentIndex = readFirstInvocationARB(index);
        if (currentIndex == index)
            return currentIndex;
    }
}
#endif

vec3 UnpackR11G11B10(uint v)
{
    float r = (v >> 0) & ((1u << 11) - 1);
    float g = (v >> 11) & ((1u << 11) - 1);
    float b = (v >> 22) & ((1u << 10) - 1);

    r *= (1.0 / float((1u << 11) - 1));
    g *= (1.0 / float((1u << 11) - 1));
    b *= (1.0 / float((1u << 10) - 1));

    return vec3(r, g, b);
}