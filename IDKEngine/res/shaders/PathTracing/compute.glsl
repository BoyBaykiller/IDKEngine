#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -3.4028235e+38
#define EPSILON 0.001
#define PI 3.14159265
#extension GL_ARB_bindless_texture : require
#extension GL_NV_gpu_shader5 : enable
#ifndef GL_NV_gpu_shader5
    #extension GL_EXT_nonuniform_qualifier : require
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

    sampler2D Metallic;
    float _pad2;

    sampler2D Roughness;
    float _pad3;

    sampler2D Specular;
    float _pad4;
};

struct Mesh
{
    mat4 Model[1];
    int MaterialIndex;
    int BaseNode;
    int _pad0;
    int _pad1;
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

    vec3 BiTangent;
    float _pad4;
};

struct HitInfo
{
    float T;
    vec3 Bary;
    int VerticesStart;
    int MeshID;
};

struct Ray
{
    vec3 Origin;
    vec3 Direction;
};

struct Node
{
    vec3 Min;
    int VerticesStart;
    vec3 Max;
    int VerticesEnd;
};

layout(std430, binding = 1) restrict readonly buffer BVHSSBO
{
    Node Nodes[];
} bvhSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 3) restrict readonly buffer VertecisSSBO
{
    Vertex Vertecis[];
} vertecisSSBO;

layout(std430, binding = 4) restrict readonly buffer IndicisSSBO
{
    uint Indicis[];
} indicisSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

layout(std140, binding = 1) uniform MaterialUBO
{
    Material Materials[384];
} materialUBO;

layout(std140, binding = 3) uniform LightsUBO
{
    Light Lights[128];
    int LightCount;
} lightsUBO;

vec3 Radiance(Ray ray);
vec3 BRDF(vec3 incomming, float specularChance, float roughness, vec3 normal, out float rayProbability);
float FresnelSchlick(float cosTheta, float n1, float n2);
bool RayTrace(Ray ray, out HitInfo hitInfo);
bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec4 baryT);
bool RayCuboidIntersect(Ray ray, Node node, out float t2);
vec3 Interpolate(vec3 v0, vec3 v1, vec3 v2, vec3 bary);
vec2 Interpolate(vec2 v0, vec2 v1, vec2 v2, vec3 bary);
Ray WorldSpaceRayToLocal(Ray ray, mat4 invModel);
vec3 CosineSampleHemisphere(vec3 normal);
vec2 UniformSampleUnitCircle();
uint GetPCGHash(inout uint seed);
float GetRandomFloat01();
vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords);

uniform int RayDepth = 6;
uniform int SPP = 1;
uniform float FocalLength = 10.0;
uniform float ApertureDiameter = 0.07; // 0.07
layout(location = 0) uniform int ThisRendererFrame;

uint rndSeed;
void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(imgCoord, imgResultSize)))
        return;

    rndSeed = ThisRendererFrame;
    // rndSeed = gl_GlobalInvocationID.x * 1973 + gl_GlobalInvocationID.y * 9277 + ThisRendererFrame;

    vec3 irradiance = vec3(0.0);
    for (int i = 0; i < SPP; i++)
    {   
        vec2 subPixelOffset = vec2(GetRandomFloat01(), GetRandomFloat01()) - 0.5; // integrating over whole pixel eliminates aliasing
        vec2 ndc = (imgCoord + subPixelOffset) / imgResultSize * 2.0 - 1.0;
        Ray rayEyeToWorld = Ray(basicDataUBO.ViewPos, GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc));

        vec3 focalPoint = rayEyeToWorld.Origin + rayEyeToWorld.Direction * FocalLength;
        vec2 offset = ApertureDiameter * 0.5 * UniformSampleUnitCircle();
        
        rayEyeToWorld.Origin = (basicDataUBO.InvView * vec4(offset, 0.0, 1.0)).xyz;
        rayEyeToWorld.Direction = normalize(focalPoint - rayEyeToWorld.Origin);

        irradiance += Radiance(rayEyeToWorld);
    }
    irradiance /= SPP;
    
    vec3 lastFrameColor = imageLoad(ImgResult, imgCoord).rgb;
    irradiance = mix(lastFrameColor, irradiance, 1.0 / (ThisRendererFrame + 1.0));
    imageStore(ImgResult, imgCoord, vec4(irradiance, 1.0));
}

vec3 Radiance(Ray ray)
{
    uvec2 handle;
    sampler2D tex = sampler2D(handle);

    vec3 throughput = vec3(1.0);
    vec3 radiance = vec3(0.0);

    HitInfo hitInfo;
    for (int i = 0; i < RayDepth; i++)
    {
        if (RayTrace(ray, hitInfo))
        {
            Vertex v0 = vertecisSSBO.Vertecis[hitInfo.VerticesStart + 0];
            Vertex v1 = vertecisSSBO.Vertecis[hitInfo.VerticesStart + 1];
            Vertex v2 = vertecisSSBO.Vertecis[hitInfo.VerticesStart + 2];
            
            mat4 model = meshSSBO.Meshes[hitInfo.MeshID].Model[0];

            vec3 tangent = Interpolate(v0.Tangent, v1.Tangent, v2.Tangent, hitInfo.Bary);
            vec3 normal = Interpolate(v0.Normal, v1.Normal, v2.Normal, hitInfo.Bary);
            vec2 texCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);

            vec3 T = normalize(vec3(model * vec4(tangent, 0.0)));
            vec3 N = normalize(vec3(model * vec4(normal, 0.0)));
            T = normalize(T - dot(T, N) * N);
            vec3 B = cross(N, T); // interpolating BiTangent would also work
            mat3 TBN = mat3(T, B, N);

            float specularChance, roughness;
            vec3 albedo;
            Material material = materialUBO.Materials[meshSSBO.Meshes[hitInfo.MeshID].MaterialIndex]; // MaterialIndex is same for v0, v1, v2
            #ifdef GL_NV_gpu_shader5
                specularChance = texture(material.Specular, texCoord).r;
                roughness = texture(material.Roughness, texCoord).r;
                normal = texture(material.Normal, texCoord).rgb;
                vec4 temp = texture(material.Albedo, texCoord);
                albedo = temp.rgb;
            #else
                specularChance = texture(nonuniformEXT(material.Specular), texCoord).r;
                roughness = texture(nonuniformEXT(material.Roughness), texCoord).r;
                normal = texture(nonuniformEXT(material.Normal), texCoord).rgb;
                vec4 temp = texture(nonuniformEXT(material.Albedo), texCoord);
                albedo = temp.rgb;
            #endif
            normal = TBN * (normal * 2.0 - 1.0);

            vec3 hitpos = ray.Origin + ray.Direction * hitInfo.T;
            float rayProbability;

            ray.Direction = BRDF(ray.Direction, specularChance, roughness, normal, rayProbability); // CosineSampleHemisphere(normal)
            ray.Origin = hitpos + ray.Direction * EPSILON; // offset ray a bit to avoid numerical errors

            radiance += ((hitInfo.MeshID > 1 && hitInfo.MeshID < 26) ? vec3(albedo * 16) : vec3(0.0)) * throughput;
            throughput *= albedo;
            throughput /= rayProbability;

            // DEBUG: Render wireframe
            // return float(any(lessThan(hitInfo.Bary, vec3(0.01)))).xxx;

            // Russian Roulette - unbiased method to terminate rays and therefore lower render times (also reduces fireflies)
            float p = max(throughput.x, max(throughput.y, throughput.z));
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
    vec3 outgoing = vec3(0.0);
    if (specularChance > raySelectRoll)
    {
        vec3 reflectionRayDir = reflect(incomming, normal);
        reflectionRayDir = normalize(mix(reflectionRayDir, diffuseRay, roughness)); 
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
    hitInfo.T = FLOAT_MAX;
    float t2;
    vec4 baryT;

    for (int i = 0; i < meshSSBO.Meshes.length(); i++)
    {
        Mesh mesh = meshSSBO.Meshes[i];
        Node root = bvhSSBO.Nodes[mesh.BaseNode];
        Ray localRay = WorldSpaceRayToLocal(ray, inverse(mesh.Model[0]));

        if (RayCuboidIntersect(localRay, root, t2) && t2 > 0.0)
        {
            for (int j = root.VerticesStart; j < root.VerticesEnd; j += 3)
            {
                vec3 v0 = vertecisSSBO.Vertecis[j + 0].Position;
                vec3 v1 = vertecisSSBO.Vertecis[j + 1].Position;
                vec3 v2 = vertecisSSBO.Vertecis[j + 2].Position;
                if (RayTriangleIntersect(localRay, v0, v1, v2, baryT) && baryT.w > 0.0 && baryT.w < hitInfo.T)
                {
                    hitInfo.Bary = baryT.xyz;
                    hitInfo.T = baryT.w;
                    hitInfo.VerticesStart = j;
                    hitInfo.MeshID = i;
                }
            }

            // int box_index_next = mesh.BaseNode;
            // for (int box_index = 0; box_index < boxes_count; box_index++) {
            //     if (box_index != box_index_next) {
            //         continue;
            //     }

            //     Node node = bvhSSBO.Nodes[box_index];

            //     bool hit = RayCuboidIntersect(node, localRay);
            //     bool leaf = node.VerticesEnd != -1;

            //     if (hit) {
            //         box_index_next = node.links.x; // hit link
            //     } else {
            //         box_index_next = node.links.y; // miss link
            //     }

            //     if (hit && leaf) {
            //         for (int j = node.VerticesStart; j < node.VerticesEnd; j++) {
                        
            //         }
            //     }
            // }


            // int nodeIndex = mesh.BaseNode;
            // while (nodeIndex != -1)
            // {
            //     node = bvhSSBO.Nodes[nodeIndex];
            //     if (intersect(node.bonding, ray))
            //     {
            //         const isLeaf = node.VerticesStart != -1;
            //         if (isLeaf)
            //         {
            //             // Triangles
            //             for (int j = node.VerticesStart; j < node.VerticesEnd; j++)
            //             {
                            
            //             }
            //         }
            //         node = node.HitLink;
            //     } 
            //     else
            //     {
            //         node = node.MissLink;
            //     }
            // }
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

    t1 = max(t1, max(tsmaller.x, max(tsmaller.y, tsmaller.z)));
    t2 = min(t2, min(tbigger.x, min(tbigger.y, tbigger.z)));
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
    return float(GetPCGHash(rndSeed)) / 4294967296.0;
}

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords)
{
    vec4 rayEye = inverseProj * vec4(normalizedDeviceCoords, -1.0, 0.0);
    rayEye.zw = vec2(-1.0, 0.0);
    return normalize((inverseView * rayEye).xyz);
}
