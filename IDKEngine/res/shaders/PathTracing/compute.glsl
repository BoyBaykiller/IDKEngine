#version 460 core
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

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba32f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform samplerCube SamplerEnvironment;

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
    float _pad0;

    sampler2D Normal;
    float _pad1;

    sampler2D Roughness;
    float _pad3;

    sampler2D Specular;
    float _pad4;
};

struct Mesh
{
    mat4 Model;
    int MaterialIndex;
    int BaseNode;
    float Emissive;
    float NormalMapStrength;
    float pad;
    float alsoPad;
    float _pad0;
    float _pad1;
};

struct Vertex
{
    vec3 Position;
    float _pad0;

    vec2 TexCoord;
    vec2 _pad1;

    vec3 Normal;
    float _pad2;

    vec3 Tangent;
    float _pad3;
};

struct HitInfo
{
    vec3 Bary;
    float T;
    uint VertexIndex;
    int HitIndex;
};

struct Ray
{
    vec3 Origin;
    vec3 Direction;
};

struct Node
{
    vec3 Min;
    uint IsLeafAndVerticesStart;
    vec3 Max;
    uint MissLinkAndVerticesCount;
};

layout(std430, binding = 1) restrict readonly buffer BVHSSBO
{
    vec2 _pad0;
    uint TreeDepth;
    uint BitsForMissLink;
    Node Nodes[];
} bvhSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 3) restrict readonly buffer BVHVertices
{
    Vertex Vertices[];
} verticesSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

layout(std140, binding = 1) uniform MaterialUBO
{
    Material Materials[256];
} materialUBO;

layout(std140, binding = 3) uniform LightsUBO
{
    Light Lights[64];
    int Count;
} lightsUBO;

vec3 Radiance(Ray ray);
vec3 BRDF(vec3 incomming, float specularChance, float roughness, vec3 normal, out float rayProbability);
float FresnelSchlick(float cosTheta, float n1, float n2);
bool RayTrace(Ray ray, out HitInfo hitInfo);
bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec4 baryT);
bool RayCuboidIntersect(Ray ray, Node node, out float t2);
bool RaySphereIntersect(Ray ray, Light light, out float t1, out float t2);
vec3 Interpolate(vec3 v0, vec3 v1, vec3 v2, vec3 bary);
vec2 Interpolate(vec2 v0, vec2 v1, vec2 v2, vec3 bary);
Ray WorldSpaceRayToLocal(Ray ray, mat4 invModel);
vec3 CosineSampleHemisphere(vec3 normal);
vec2 UniformSampleUnitCircle();
uint GetPCGHash(inout uint seed);
float GetRandomFloat01();
vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords);
uint EmulateNonUniform(uint index);

uniform int RayDepth;
uniform float FocalLength;
uniform float ApertureDiameter;

uint rngSeed;
void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(imgCoord, imgResultSize)))
        return;

    //rngSeed = basicDataUBO.FrameCount;
    rngSeed = gl_GlobalInvocationID.x * 1973 + gl_GlobalInvocationID.y * 9277 + basicDataUBO.FrameCount * 2699 | 1;

    vec2 subPixelOffset = vec2(GetRandomFloat01(), GetRandomFloat01());
    vec2 ndc = (imgCoord + subPixelOffset) / imgResultSize * 2.0 - 1.0;
    Ray camRay = Ray(basicDataUBO.ViewPos, GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc));

    vec3 focalPoint = camRay.Origin + camRay.Direction * FocalLength;
    vec2 offset = ApertureDiameter * 0.5 * UniformSampleUnitCircle();
    
    camRay.Origin = (basicDataUBO.InvView * vec4(offset, 0.0, 1.0)).xyz;
    camRay.Direction = normalize(focalPoint - camRay.Origin);
    vec3 irradiance = Radiance(camRay);

    vec3 lastFrameColor = imageLoad(ImgResult, imgCoord).rgb;
    irradiance = mix(lastFrameColor, irradiance, 1.0 / (basicDataUBO.FrameCount + 1.0));
    imageStore(ImgResult, imgCoord, vec4(irradiance, 1.0));
}

vec3 Radiance(Ray ray)
{
    vec3 throughput = vec3(1.0);
    vec3 radiance = vec3(0.0);

    HitInfo hitInfo;
    for (int i = 0; i < RayDepth; i++)
    {
        if (RayTrace(ray, hitInfo))
        {
            // Render wireframe
            // return float(any(lessThan(hitInfo.Bary, vec3(0.01)))).xxx;

            vec3 hitpos = ray.Origin + ray.Direction * hitInfo.T;
            float specularChance = 0.0;
            float roughness = 1.0;
            float alpha = 1.0;
            vec3 albedo;
            vec3 normal;
            vec3 emissive;
            if (hitInfo.HitIndex >= 0)
            {
                Vertex v0 = verticesSSBO.Vertices[hitInfo.VertexIndex + 0u];
                Vertex v1 = verticesSSBO.Vertices[hitInfo.VertexIndex + 1u];
                Vertex v2 = verticesSSBO.Vertices[hitInfo.VertexIndex + 2u];
                
                Mesh mesh = meshSSBO.Meshes[hitInfo.HitIndex];
                mat4 model = mesh.Model;

                vec3 tangent = normalize(Interpolate(v0.Tangent, v1.Tangent, v2.Tangent, hitInfo.Bary));
                vec3 geoNormal = normalize(Interpolate(v0.Normal, v1.Normal, v2.Normal, hitInfo.Bary));
                vec2 texCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);

                vec3 T = normalize(vec3(model * vec4(tangent, 0.0)));
                vec3 N = normalize(vec3(model * vec4(geoNormal, 0.0)));
                T = normalize(T - dot(T, N) * N);
                vec3 B = cross(N, T);
                mat3 TBN = mat3(T, B, N);
                
            #ifdef GL_NV_gpu_shader5
                Material material = materialUBO.Materials[mesh.MaterialIndex];
            #else
                Material material = materialUBO.Materials[EmulateNonUniform(mesh.MaterialIndex)];
            #endif

                albedo = texture(material.Albedo, texCoord).rgb;
                normal = texture(material.Normal, texCoord).rgb;
                specularChance = texture(material.Specular, texCoord).r;
                roughness = texture(material.Roughness, texCoord).r;
                emissive = mesh.Emissive * albedo;

                normal = TBN * normalize(normal * 2.0 - 1.0);
                normal = normalize(mix(geoNormal, normal, mesh.NormalMapStrength));

                if (dot(-ray.Direction, normal) < 0.0)
                    normal *= -1.0;
            }
            else
            {
                Light light = lightsUBO.Lights[-hitInfo.HitIndex - 1];
                emissive = light.Color;
                albedo = light.Color;
                normal = (hitpos - light.Position) / light.Radius;
            }

            // TOOD: Implement BSDF
            float rayProbability;
            ray.Direction = BRDF(ray.Direction, specularChance, roughness, normal, rayProbability);
            ray.Origin = hitpos + ray.Direction * EPSILON;

            radiance += emissive * throughput;
            throughput *= albedo;
            throughput /= rayProbability;

            // Russian Roulette - unbiased method to terminate rays and therefore lower render times (also reduces fireflies)
        #if GL_AMD_shader_trinary_minmax
            float p = max3(throughput.x, throughput.y, throughput.z);
        #else
            float p = max(throughput.x, max(throughput.y, throughput.z));
        #endif
            if (GetRandomFloat01() > p)
                break;

            throughput /= p;
        }
        else
        {
            radiance += texture(SamplerEnvironment, ray.Direction).rgb * throughput;
            break;
        }
    }

    return radiance;
}

vec3 BRDF(vec3 incomming, float specularChance, float roughness, vec3 normal, out float rayProbability)
{
    if (specularChance > 0.0)
    {
        specularChance = mix(specularChance, 1.0, FresnelSchlick(dot(-incomming, normal), 1.0, 1.0));
    }

    vec3 diffuseRay = CosineSampleHemisphere(normal);
    
    float raySelectRoll = GetRandomFloat01();
    vec3 outgoing;
    if (specularChance > raySelectRoll)
    {
        vec3 reflectionRayDir = reflect(incomming, normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRay, roughness * roughness)); 
        outgoing = reflectionRayDir;
        rayProbability = specularChance;
    }
    else
    {
        outgoing = diffuseRay;
        rayProbability = 1.0 - specularChance;
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

bool RayTrace(Ray ray, out HitInfo hitInfo)
{
    const uint bitsForMissLink = bvhSSBO.BitsForMissLink; 
    const uint treeDepth = bvhSSBO.TreeDepth;

    hitInfo.T = FLOAT_MAX;
    float t2;
    float nodeTMin = FLOAT_MAX;
    vec4 baryT;

    for (int i = 0; i < meshSSBO.Meshes.length(); i++)
    {
        Mesh mesh = meshSSBO.Meshes[i];
        Ray localRay = WorldSpaceRayToLocal(ray, inverse(mesh.Model));
        
        uint localNodeIndex = 0u;
        while (localNodeIndex < (1u << treeDepth) - 1u)
        {
            Node node = bvhSSBO.Nodes[mesh.BaseNode + localNodeIndex];
            if (RayCuboidIntersect(localRay, node, t2) && t2 > 0.0)
            {
                if (bool(node.IsLeafAndVerticesStart))
                {
                    const uint MAX_COUNT = (1u << (32u - bitsForMissLink)) - 1u;
                    const uint count = node.MissLinkAndVerticesCount & MAX_COUNT;
                    
                    const uint MAX_START = (1u << 31u) - 1u;
                    const uint start = node.IsLeafAndVerticesStart & MAX_START;
                    
                    for (uint j = start; j < start + count; j += 3u)
                    {
                        if (RayTriangleIntersect(localRay, verticesSSBO.Vertices[j + 0u].Position, verticesSSBO.Vertices[j + 1u].Position, verticesSSBO.Vertices[j + 2u].Position, baryT) && baryT.w > 0.0 && baryT.w < hitInfo.T)
                        {
                            hitInfo.Bary = baryT.xyz;
                            hitInfo.T = baryT.w;
                            hitInfo.VertexIndex = j;
                            hitInfo.HitIndex = i;
                        }
                    }
                }
                localNodeIndex++;
            }
            else
            {
                localNodeIndex = node.MissLinkAndVerticesCount >> (32u - bitsForMissLink);
            }
        }
    }

    float t1;
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        Light light = lightsUBO.Lights[i];
        if (RaySphereIntersect(ray, light, t1, t2) && t2 > 0.0 && t1 < hitInfo.T)
        {
            hitInfo.T = t1;
            hitInfo.HitIndex = -i - 1;
        }
    }

    return hitInfo.T != FLOAT_MAX;
}

bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec4 baryT)
{
    // Source: https://www.iquilezles.org/www/articles/intersectors/intersectors.htm

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

bool RayCuboidIntersect(Ray ray, Node node, out float t2)
{
    // Source: https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525
    float t1 = FLOAT_MIN;
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

bool RaySphereIntersect(Ray ray, Light light, out float t1, out float t2)
{
    // Source: https://antongerdelan.net/opengl/raycasting.html
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

vec3 CosineSampleHemisphere(vec3 normal)
{
    // Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/

    float z = GetRandomFloat01() * 2.0 - 1.0;
    float a = GetRandomFloat01() * 2.0 * PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);

    // Convert unit vector in sphere to a cosine weighted vector in hemisphere
    return normalize(normal + vec3(x, y, z));
}

vec2 UniformSampleUnitCircle()
{
    float angle = GetRandomFloat01() * 2.0 * PI;
    float r = sqrt(GetRandomFloat01());
    return vec2(cos(angle), sin(angle)) * r;
}

// Faster and much more random than Wang Hash
// See: https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/
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
    uint currentIndex;
    while ((currentIndex = readFirstInvocationARB(index)) != index) ;
    return index;
}
#endif