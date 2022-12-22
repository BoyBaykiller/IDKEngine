#version 460 core
#define N_HIT_PROGRAM_LOCAL_SIZE_X 64 // used in shader and client code - keep in sync!
#define EMISSIVE_MATERIAL_MULTIPLIER 5.0
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX
#define EPSILON 0.001
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require
#extension GL_AMD_gpu_shader_half_float : enable
#extension GL_AMD_gpu_shader_half_float_fetch : enable // requires GL_AMD_gpu_shader_half_float

#ifdef GL_AMD_gpu_shader_half_float_fetch
#define HF_SAMPLER_2D f16sampler2D
#else
#define HF_SAMPLER_2D sampler2D
#endif

// Inserted by application. Positive integral expression
#define MAX_BLAS_TREE_DEPTH __maxBlasTreeDepth__

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict readonly writeonly uniform image2D ImgResult;

struct Material
{
    HF_SAMPLER_2D Albedo;
    HF_SAMPLER_2D Normal;
    HF_SAMPLER_2D Roughness;
    HF_SAMPLER_2D Specular;
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
    int TriangleIndex;
    uint MeshIndex;
    uint InstanceID;
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
    float _pad0;

    vec3 Direction;
    float _pad1;

    vec3 Throughput;
    float PrevIOROrDebugNodeCounter;

    vec3 Radiance;
    bool IsRefractive;
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
    float _pad0;
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
    uint Counts[2];
    uint AccumulatedSamples;
    uint Indices[];
} rayIndicesSSBO;

layout(std430, binding = 8) restrict buffer DispatchCommandSSBO
{
    DispatchCommand DispatchCommands[2];
} dispatchCommandSSBO;

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

layout(std140, binding = 2) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

bool TraceRay(inout TransportRay transportRay);
vec3 BSDF(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, float prevIor, vec3 normal, out float rayProbability, out float newIor, out bool isRefractive, out bool fromInside);
float FresnelSchlick(float cosTheta, float n1, float n2);
bool ClosestHit(Ray ray, out HitInfo hitInfo, inout uint interiorNodeCounter);
bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec4 baryT);
bool RayCuboidIntersect(Ray ray, Node node, out float t1, out float t2);
bool RaySphereIntersect(Ray ray, Light light, out float t1, out float t2);
vec3 Interpolate(vec3 v0, vec3 v1, vec3 v2, vec3 bary);
vec2 Interpolate(vec2 v0, vec2 v1, vec2 v2, vec3 bary);
Ray WorldSpaceRayToLocal(Ray ray, mat4 invModel);
vec3 UniformSampleSphere();
vec3 CosineSampleHemisphere(vec3 normal);
vec2 UniformSampleDisk();
uint GetPCGHash(inout uint seed);
float GetRandomFloat01();
vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords);
vec3 DecompressSNorm32Fast(uint data);
ivec2 ReorderInvocations(uint n);

uniform bool IsDebugBVHTraversal;
uniform bool IsTraceLights;
uniform float FocalLength;
uniform float ApertureDiameter;
uniform float RayCoherency;

shared uint SharedStack[gl_WorkGroupSize.x * gl_WorkGroupSize.y][MAX_BLAS_TREE_DEPTH];

uint rngSeed;
void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ReorderInvocations(20);
    if (any(greaterThanEqual(imgCoord, imgResultSize)))
        return;

    rngSeed = imgCoord.x * 312 + imgCoord.y * 291 + rayIndicesSSBO.AccumulatedSamples * 2699;
    if (GetRandomFloat01() < RayCoherency)
        rngSeed = rayIndicesSSBO.AccumulatedSamples * 2699;

    vec2 subPixelOffset = vec2(GetRandomFloat01(), GetRandomFloat01());
    vec2 ndc = (imgCoord + subPixelOffset) / imgResultSize * 2.0 - 1.0;

    vec3 camDir = GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc);
    vec3 focalPoint = basicDataUBO.ViewPos + camDir * FocalLength;
    vec3 aperturePoint = (basicDataUBO.InvView * vec4(ApertureDiameter * 0.5 * UniformSampleDisk(), 0.0, 1.0)).xyz;

    camDir = normalize(focalPoint - aperturePoint);

    TransportRay transportRay;
    transportRay.Origin = aperturePoint;
    transportRay.Direction = camDir;

    transportRay.Throughput = vec3(1.0);
    transportRay.Radiance = vec3(0.0);
    transportRay.PrevIOROrDebugNodeCounter = 1.0;
    transportRay.IsRefractive = false;
    
    uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;

    bool continueRay = TraceRay(transportRay);
    transportRaySSBO.Rays[rayIndex] = transportRay;

    if (continueRay)
    {
        uint index = atomicAdd(rayIndicesSSBO.Counts[1], 1u);
        rayIndicesSSBO.Indices[index] = rayIndex;

        if (index % N_HIT_PROGRAM_LOCAL_SIZE_X == 0)
        {
            atomicAdd(dispatchCommandSSBO.DispatchCommands[1].NumGroupsX, 1u);
        }
    }
}

bool TraceRay(inout TransportRay transportRay)
{
    HitInfo hitInfo;
    uint nodeCounter = 0;
    if (ClosestHit(Ray(transportRay.Origin, transportRay.Direction), hitInfo, nodeCounter))
    {
        if (IsDebugBVHTraversal)
        {
            transportRay.PrevIOROrDebugNodeCounter = nodeCounter;
            return false;
        }

        transportRay.Origin += transportRay.Direction * hitInfo.T;

        vec3 albedo;
        vec3 normal;
        vec3 emissive;
        float refractionChance;
        float specularChance;
        float roughness;
        float ior;
        vec3 absorbance;
        // This if statement somehow improves performance even if the condition is always true on RX 5700 XT
        if (hitInfo.TriangleIndex != -1)
        {
            Triangle triangle = triangleSSBO.Triangles[hitInfo.TriangleIndex];
            Vertex v0 = triangle.Vertex0;
            Vertex v1 = triangle.Vertex1;
            Vertex v2 = triangle.Vertex2;

            vec2 texCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);
            vec3 geoNormal = normalize(Interpolate(DecompressSNorm32Fast(v0.Normal), DecompressSNorm32Fast(v1.Normal), DecompressSNorm32Fast(v2.Normal), hitInfo.Bary));
            vec3 tangent = normalize(Interpolate(DecompressSNorm32Fast(v0.Tangent), DecompressSNorm32Fast(v1.Tangent), DecompressSNorm32Fast(v2.Tangent), hitInfo.Bary));
        
            mat4 model = matrixSSBO.Models[hitInfo.InstanceID];
            vec3 T = normalize((model * vec4(tangent, 0.0)).xyz);
            vec3 N = normalize((model * vec4(geoNormal, 0.0)).xyz);
            T = normalize(T - dot(T, N) * N);
            vec3 B = cross(N, T);
            mat3 TBN = mat3(T, B, N);

            Mesh mesh = meshSSBO.Meshes[hitInfo.MeshIndex];
            // If no GL_NV_gpu_shader5 this is UB due to non dynamically uniform indexing
            // Can't use GL_EXT_nonuniform_qualifier because only modern amd drivers get the implementation right without compile errors
            Material material = materialSSBO.Materials[mesh.MaterialIndex];

            vec4 albedoAlpha = texture(material.Albedo, texCoord);
            albedo = albedoAlpha.rgb;
            refractionChance = clamp((1.0 - albedoAlpha.a) + mesh.RefractionChance, 0.0, 1.0);
            emissive = (texture(material.Emissive, texCoord).rgb * EMISSIVE_MATERIAL_MULTIPLIER + mesh.EmissiveBias) * albedo;
            specularChance = clamp(texture(material.Specular, texCoord).r + mesh.SpecularBias, 0.0, 1.0 - refractionChance);
            roughness = clamp(texture(material.Roughness, texCoord).r + mesh.RoughnessBias, 0.0, 1.0);
            normal = texture(material.Normal, texCoord).rgb;        
            normal = TBN * normalize(normal * 2.0 - 1.0);
            normal = normalize(mix(mat3(transpose(inverse(model))) * geoNormal, normal, mesh.NormalMapStrength));
            ior = mesh.IOR;
            absorbance = mesh.Absorbance;
        }
        else if (IsTraceLights)
        {
            Light light = lightsUBO.Lights[hitInfo.MeshIndex];
            emissive = light.Color;
            albedo = light.Color;
            normal = (transportRay.Origin - light.Position) / light.Radius;

            refractionChance = 0.0;
            specularChance = 0.0;
            roughness = 0.0;
            ior = 1.0;
            absorbance = vec3(0.0);
        }

        bool fromInside;
        float rayProbability, newIor;
        transportRay.Direction = BSDF(transportRay.Direction, specularChance, roughness, refractionChance, ior, transportRay.PrevIOROrDebugNodeCounter, normal, rayProbability, newIor, transportRay.IsRefractive, fromInside);
        transportRay.Origin += transportRay.Direction * EPSILON;
        transportRay.PrevIOROrDebugNodeCounter = newIor; // ior of the object we are currently in

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
            return false;
        transportRay.Throughput /= p;

        return true;
    }
    else
    {
        transportRay.Radiance += texture(skyBoxUBO.Albedo, transportRay.Direction).rgb * transportRay.Throughput;
        return false;
    }
}

vec3 BSDF(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, float prevIor, vec3 normal, out float rayProbability, out float newIor, out bool isRefractive, out bool fromInside)
{
    float cosTheta = dot(-incomming, normal);
    fromInside = cosTheta < 0.0;
    if (fromInside)
        normal *= -1.0;

    isRefractive = false;
    if (specularChance > 0.0) // adjust specular chance based on view angle
    {
        specularChance = mix(specularChance, 1.0, FresnelSchlick(cosTheta, fromInside ? ior : prevIor, fromInside ? prevIor : ior));
        float diffuseChance = 1.0 - specularChance - refractionChance;
        refractionChance = 1.0 - specularChance - diffuseChance;
    }

    float raySelectRoll = GetRandomFloat01();
    vec3 diffuseRayDir = CosineSampleHemisphere(normal);
    vec3 outgoing;
    if (specularChance > raySelectRoll)
    {
        vec3 reflectionRayDir = reflect(incomming, normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRayDir, roughness * roughness));
        outgoing = reflectionRayDir;
        rayProbability = specularChance;
        newIor = fromInside ? ior : 1.0;
    }
    else if (specularChance + refractionChance > raySelectRoll)
    {
        vec3 refractionRayDir = refract(incomming, normal, fromInside ? (ior / prevIor) : (prevIor / ior));
        isRefractive = refractionRayDir != vec3(0.0);
        if (!isRefractive) // Total Internal Reflection
        {
            refractionRayDir = reflect(incomming, normal);
            refractionRayDir = normalize(mix(refractionRayDir, diffuseRayDir, roughness * roughness));
        }
        refractionRayDir = normalize(mix(refractionRayDir, isRefractive ? -diffuseRayDir : diffuseRayDir, roughness * roughness));
        outgoing = refractionRayDir;
        rayProbability = refractionChance;

        if (fromInside)
            newIor = isRefractive ? 1.0 : ior;
        else
            newIor = ior;
    }
    else
    {
        outgoing = diffuseRayDir;
        rayProbability = 1.0 - specularChance - refractionChance;
        newIor = fromInside ? ior : 1.0;
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

        if (sinT2 > 1.0)
            return 1.0;
        cosTheta = sqrt(1.0 - sinT2);
    }

    return r0 + (1.0 - r0) * pow(1.0 - cosTheta, 5.0);
}

bool ClosestHit(Ray ray, out HitInfo hitInfo, inout uint interiorNodeCounter)
{
    hitInfo.T = FLOAT_MAX;
    hitInfo.TriangleIndex = -1;
    float rayTMin, rayTMax;

    if (IsTraceLights)
    {
        for (int i = 0; i < lightsUBO.Count; i++)
        {
            Light light = lightsUBO.Lights[i];
            if (RaySphereIntersect(ray, light, rayTMin, rayTMax) && rayTMax > 0.0 && rayTMin < hitInfo.T)
            {
                hitInfo.T = rayTMin;
                hitInfo.MeshIndex = i;
            }
        }
    }

    vec4 baryT;
    for (uint i = 0; i < meshSSBO.Meshes.length(); i++)
    {
        DrawCommand cmd = drawCommandSSBO.DrawCommands[i];
        uint baseNode = 2 * (cmd.FirstIndex / 3);

        const uint glInstanceID = 0; // TODO: Work out actual instanceID value
        Ray localRay = WorldSpaceRayToLocal(ray, inverse(matrixSSBO.Models[cmd.BaseInstance + glInstanceID]));

        uint stackPtr = 0;
        uint stackTop = 0;
        while (true)
        {
            Node node = blasSSBO.Nodes[baseNode + stackTop];
            if (!(RayCuboidIntersect(localRay, node, rayTMin, rayTMax) && rayTMax > 0.0 && rayTMin < hitInfo.T))
            {
                if (stackPtr == 0) break;
                stackTop = SharedStack[gl_LocalInvocationIndex][--stackPtr];
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
                        hitInfo.MeshIndex = i;
                        hitInfo.TriangleIndex = int(j);
                        hitInfo.InstanceID = cmd.BaseInstance + glInstanceID;
                    }
                }
            }
            else
            {
                interiorNodeCounter++;
                float tMinLeft;
                float tMinRight;

                bool leftChildHit = RayCuboidIntersect(localRay, blasSSBO.Nodes[baseNode + node.TriStartOrLeftChild], tMinLeft, rayTMax) && rayTMax > 0.0 && tMinLeft < hitInfo.T;
                bool rightChildHit = RayCuboidIntersect(localRay, blasSSBO.Nodes[baseNode + node.TriStartOrLeftChild + 1], tMinRight, rayTMax) && rayTMax > 0.0 && tMinRight < hitInfo.T;

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        stackTop = node.TriStartOrLeftChild + (1 - int(tMinLeft < tMinRight));
                        SharedStack[gl_LocalInvocationIndex][stackPtr++] = node.TriStartOrLeftChild + int(tMinLeft < tMinRight);
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
            stackTop = SharedStack[gl_LocalInvocationIndex][--stackPtr];
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

    t1 = max(t1, max(tsmaller.x, max(tsmaller.y, tsmaller.z)));
    t2 = min(t2, min(tbigger.x, min(tbigger.y, tbigger.z)));

    return t1 <= t2;
}

// Source: https://antongerdelan.net/opengl/raycasting.html
bool RaySphereIntersect(Ray ray, Light light, out float t1, out float t2)
{
    t1 = FLOAT_MAX;
    t2 = FLOAT_MAX;

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

vec2 UniformSampleDisk()
{
    vec2 point;
    float dist;
    do
    {
        point = vec2(GetRandomFloat01(), GetRandomFloat01()) * 2.0 - 1.0;
        dist = dot(point, point);
    } while (dist > 1.0);

    return point;
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

vec3 DecompressSNorm32Fast(uint data)
{
    float r = (data >> 0) & ((1u << 11) - 1);
    float g = (data >> 11) & ((1u << 11) - 1);
    float b = (data >> 22) & ((1u << 10) - 1);

    r /= (1u << 11) - 1;
    g /= (1u << 11) - 1;
    b /= (1u << 10) - 1;

    return vec3(r, g, b) * 2.0 - 1.0;
}

// Source: https://youtu.be/HgisCS30yAI
// Info: https://developer.nvidia.com/blog/optimizing-compute-shaders-for-l2-locality-using-thread-group-id-swizzling/
ivec2 ReorderInvocations(uint n)
{
    uint idx = gl_WorkGroupID.y * gl_NumWorkGroups.x + gl_WorkGroupID.x;

    uint columnSize = gl_NumWorkGroups.y * n;
    uint fullColumnCount = gl_NumWorkGroups.x / n;
    uint lastColumnWidth = gl_NumWorkGroups.x % n;

    uint columnIdx = idx / columnSize;
    uint idxInColumn = idx % columnSize;

    uint columnWidth = n;
    if (columnIdx == fullColumnCount)
    {
        columnWidth = lastColumnWidth;
    } 

    uvec2 workGroupSwizzled;
    workGroupSwizzled.y = idxInColumn / columnWidth;
    workGroupSwizzled.x = idxInColumn % columnWidth + columnIdx * n;

    ivec2 pos = ivec2(workGroupSwizzled * gl_WorkGroupSize.xy + gl_LocalInvocationID.xy);
    return pos;
}