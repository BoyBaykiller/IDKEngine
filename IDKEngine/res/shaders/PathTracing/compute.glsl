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

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

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
    float TexCoordU;

    vec3 Normal;
    float TexCoordV;

    vec3 Tangent;
    float _pad0;
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

layout(std430, binding = 0) restrict readonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandsSSBO;

layout(std430, binding = 1) restrict readonly buffer BVHSSBO
{
    Node Nodes[];
} bvhSSBO;

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

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FreezeFramesCounter;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
} basicDataUBO;


layout(std140, binding = 2) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

vec3 Radiance(Ray ray);
vec3 BSDF(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, vec3 normal, out float rayProbability, out bool isRefractive);
float FresnelSchlick(float cosTheta, float n1, float n2);
bool TraceRay(Ray ray, out HitInfo hitInfo);
bool RayTriangleIntersect(Ray ray, vec3 v0, vec3 v1, vec3 v2, out vec4 baryT);
bool RayCuboidIntersect(Ray ray, Node node, out float t1, out float t2);
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
vec3 SpectralJet(float w);

uniform int RayDepth;
uniform float FocalLength;
uniform float ApertureDiameter;
uniform bool IsDebugBVHTraversal;

uint rngSeed;
uint debugBLASCounter = 0;
void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(imgCoord, imgResultSize)))
        return;

    //rngSeed = basicDataUBO.FreezeFramesCounter;
    rngSeed = gl_GlobalInvocationID.x * 1973 + gl_GlobalInvocationID.y * 9277 + basicDataUBO.FreezeFramesCounter * 2699 | 1;

    vec2 subPixelOffset = IsDebugBVHTraversal ? vec2(0.5) : vec2(GetRandomFloat01(), GetRandomFloat01());
    vec2 ndc = (imgCoord + subPixelOffset) / imgResultSize * 2.0 - 1.0;
    Ray camRay = Ray(basicDataUBO.ViewPos, GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc));

    vec3 focalPoint = camRay.Origin + camRay.Direction * FocalLength;
    vec2 offset = ApertureDiameter * 0.5 * UniformSampleUnitCircle();
    
    camRay.Origin = (basicDataUBO.InvView * vec4(offset, 0.0, 1.0)).xyz;
    camRay.Direction = normalize(focalPoint - camRay.Origin);
    vec3 irradiance = Radiance(camRay);
    if (IsDebugBVHTraversal)
    {
        // use visible light spectrum as heatmap
        float waveLength = min(debugBLASCounter * 2.5 + 400.0, 700.0);
        vec3 col = SpectralJet(waveLength);
        irradiance = col;
    }

    vec3 lastFrameColor = imageLoad(ImgResult, imgCoord).rgb;
    irradiance = mix(lastFrameColor, irradiance, 1.0 / (basicDataUBO.FreezeFramesCounter + 1.0));
    imageStore(ImgResult, imgCoord, vec4(irradiance, 1.0));
}

vec3 Radiance(Ray ray)
{
    vec3 throughput = vec3(1.0);
    vec3 radiance = vec3(0.0);

    HitInfo hitInfo;
    bool isRefractive;
    for (int i = 0; i < RayDepth; i++)
    {
        if (TraceRay(ray, hitInfo))
        {
            // Render wireframe
            // return float(any(lessThan(hitInfo.Bary, vec3(0.01)))).xxx;

            vec3 hitpos = ray.Origin + ray.Direction * hitInfo.T;
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

                vec2 texCoord = Interpolate(vec2(v0.TexCoordU, v0.TexCoordV), vec2(v1.TexCoordU, v1.TexCoordV), vec2(v2.TexCoordU, v2.TexCoordV), hitInfo.Bary);
                vec3 geoNormal = normalize(Interpolate(v0.Normal, v1.Normal, v2.Normal, hitInfo.Bary));
                vec3 tangent = normalize(Interpolate(v0.Tangent, v1.Tangent, v2.Tangent, hitInfo.Bary));

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

            if (isRefractive)
            {
                throughput *= exp(-absorbance * hitInfo.T);
            }

            float rayProbability;
            ray.Direction = BSDF(ray.Direction, specularChance, roughness, refractionChance, ior, normal, rayProbability, isRefractive);
            ray.Origin = hitpos + ray.Direction * EPSILON;
            
            radiance += emissive * throughput;
            if (!isRefractive)
            {
                throughput *= albedo;
            }
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

vec3 BSDF(vec3 incomming, float specularChance, float roughness, float refractionChance, float ior, vec3 normal, out float rayProbability, out bool isRefractive)
{
    float cosTheta = dot(-incomming, normal);
    bool fromInside = cosTheta < 0.0;
    if (fromInside)
        normal *= -1.0;

    isRefractive = false;
    if (specularChance > 0.0)
    {
        specularChance = mix(specularChance, 1.0, FresnelSchlick(cosTheta, fromInside ? ior : 1.0, fromInside ? 1.0 : ior));
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
        vec3 refractionRayDir = refract(incomming, normal, fromInside ? (ior / 1.0) : (1.0 / ior));
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
    return r0 + (1.0 - r0) * pow(1.0 - cosTheta, 5.0);
}

bool TraceRay(Ray ray, out HitInfo hitInfo)
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
    float nodeTMin, nodeTMax;
    for (int i = 0; i < meshSSBO.Meshes.length(); i++)
    {
        DrawCommand cmd = drawCommandsSSBO.DrawCommands[i];
        int baseNode = 2 * (cmd.FirstIndex / 3);
        
        const int glInstanceID = 0; // TODO: Work out actual instanceID value
        Ray localRay = WorldSpaceRayToLocal(ray, inverse(matrixSSBO.Models[cmd.BaseInstance + glInstanceID]));
        
        uint stack[32];
        uint stackPtr = 0;
        uint stackTop = 0;
        while (true)
        {
            Node node = bvhSSBO.Nodes[baseNode + stackTop];
            if (!(RayCuboidIntersect(localRay, node, nodeTMin, nodeTMax) && nodeTMax > 0.0 && nodeTMin < hitInfo.T))
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
                debugBLASCounter++;

                float dist0;
                float dist1;

                bool leftChildHit = RayCuboidIntersect(localRay, bvhSSBO.Nodes[baseNode + node.TriStartOrLeftChild], dist0, nodeTMax) && nodeTMax > 0.0 && dist0 < hitInfo.T;
                bool rightChildHit = RayCuboidIntersect(localRay, bvhSSBO.Nodes[baseNode + node.TriStartOrLeftChild + 1], dist1, nodeTMax) && nodeTMax > 0.0 && dist1 < hitInfo.T;
                
                if (leftChildHit || rightChildHit)
                {
                    // Note: We add 1 to the left child to get the right child
                    // This allows to remove some branches by converting a bool to int and adding it

                    // If both children are hit assign the closest to stackTop to traverse down next
                    // and put further onto stack for traversing up if we need to
                    if (leftChildHit && rightChildHit)
                    {
                        stackTop = node.TriStartOrLeftChild + (1 - int(dist0 < dist1));
                        stack[stackPtr++] = node.TriStartOrLeftChild + int(dist0 < dist1);
                    }
                    else
                    {
                        // Assign the one child that was hit to stackTop to traverse down next
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

// Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
vec3 CosineSampleHemisphere(vec3 normal)
{
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
    for (;;)
    {
        uint currentIndex = readFirstInvocationARB(index);
        if (currentIndex == index)
            return currentIndex;
    }
}
#endif


// Source: https://www.shadertoy.com/view/ls2Bz1
vec3 SpectralJet(float w)
{
	float x = clamp((w - 400.0) / 300.0, 0.0, 1.0);
	vec3 c;

	if (x < 0.25)
		c = vec3(0.0, 4.0 * x, 1.0);
	else if (x < 0.5)
		c = vec3(0.0, 1.0, 1.0 + 4.0 * (0.25 - x));
	else if (x < 0.75)
		c = vec3(4.0 * (x - 0.5), 1.0, 0.0);
	else
		c = vec3(1.0, 1.0 + 4.0 * (0.75 - x), 0.0);

	return clamp(c, vec3(0.0), vec3(1.0));
}