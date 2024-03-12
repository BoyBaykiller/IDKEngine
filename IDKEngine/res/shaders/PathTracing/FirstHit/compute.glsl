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

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict readonly writeonly uniform image2D ImgResult;

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

    HF_SAMPLER_2D BaseColor;
    HF_SAMPLER_2D MetallicRoughness;

    HF_SAMPLER_2D Normal;
    HF_SAMPLER_2D Emissive;

    HF_SAMPLER_2D Transmission;
    uvec2 _pad0;
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

struct Vertex
{
    vec2 TexCoord;
    uint Tangent;
    uint Normal;
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

layout(std430, binding = 10) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

layout(std430, binding = 11) restrict readonly buffer VertexSSBO
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

layout(std430, binding = 8) restrict writeonly buffer WavefrontRaySSBO
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
ivec2 ReorderInvocations(uint n);

uniform bool IsDebugBVHTraversal;
uniform bool IsTraceLights;
uniform bool IsOnRefractionTintAlbedo;
uniform float FocalLength;
uniform float LenseRadius;

AppInclude(PathTracing/include/BVHIntersect.glsl)
AppInclude(PathTracing/include/RussianRoulette.glsl)

void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ReorderInvocations(20);
    if (any(greaterThanEqual(imgCoord, imgResultSize)))
    {
        return;
    }

    InitializeRandomSeed((imgCoord.y * 4096 + imgCoord.x) * (wavefrontPTSSBO.AccumulatedSamples + 1));

    vec2 subPixelOffset = vec2(GetRandomFloat01(), GetRandomFloat01());
    vec2 ndc = (imgCoord + subPixelOffset) / imgResultSize * 2.0 - 1.0;

    vec3 camDir = GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc);
    vec3 focalPoint = basicDataUBO.ViewPos + camDir * FocalLength;
    vec3 pointOnLense = (basicDataUBO.InvView * vec4(LenseRadius * UniformSampleDisk(), 0.0, 1.0)).xyz;

    camDir = normalize(focalPoint - pointOnLense);

    WavefrontRay wavefrontRay;
    wavefrontRay.Origin = pointOnLense;
    
    vec2 compressedDir = CompressOctahedron(camDir);
    wavefrontRay.CompressedDirectionX = compressedDir.x;
    wavefrontRay.CompressedDirectionY = compressedDir.y;

    wavefrontRay.Throughput = vec3(1.0);
    wavefrontRay.Radiance = vec3(0.0);
    wavefrontRay.PreviousIOROrDebugNodeCounter = 1.0;
    
    uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;

    bool continueRay = TraceRay(wavefrontRay);
    wavefrontRaySSBO.Rays[rayIndex] = wavefrontRay;

    if (continueRay)
    {
        uint index = atomicAdd(wavefrontPTSSBO.Counts[1], 1u);
        wavefrontPTSSBO.Indices[index] = rayIndex;

        if (index % N_HIT_PROGRAM_LOCAL_SIZE_X == 0)
        {
            atomicAdd(wavefrontPTSSBO.DispatchCommands[1].NumGroupsX, 1u);
        }
    }
}

bool TraceRay(inout WavefrontRay wavefrontRay)
{
    vec3 uncompressedDir = DecompressOctahedron(vec2(wavefrontRay.CompressedDirectionX, wavefrontRay.CompressedDirectionY));

    HitInfo hitInfo;
    uint debugNodeCounter = 0;
    if (BVHRayTrace(Ray(wavefrontRay.Origin, uncompressedDir), hitInfo, debugNodeCounter, IsTraceLights, FLOAT_MAX))
    {
        if (IsDebugBVHTraversal)
        {
            wavefrontRay.PreviousIOROrDebugNodeCounter = debugNodeCounter;
            return false;
        }

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

            MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[hitInfo.InstanceID];
            Mesh mesh = meshSSBO.Meshes[meshInstance.MeshIndex];
            Material material = materialSSBO.Materials[mesh.MaterialIndex];

            mat3 unitVecToWorld = mat3(transpose(meshInstance.InvModelMatrix));
            vec3 worldNormal = normalize(unitVecToWorld * interpNormal);
            vec3 worldTangent = normalize(unitVecToWorld * interpTangent);
            mat3 TBN = GetTBN(worldTangent, worldNormal);
            vec3 textureWorldNormal = texture(material.Normal, interpTexCoord).rgb;
            textureWorldNormal = TBN * (textureWorldNormal * 2.0 - 1.0);
            normal = normalize(mix(worldNormal, textureWorldNormal, mesh.NormalMapStrength));

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
            Light light = lightsUBO.Lights[hitInfo.InstanceID];
            emissive = light.Color;
            albedo = light.Color;
            normal = (wavefrontRay.Origin - light.Position) / light.Radius;
        }

        float cosTheta = dot(-uncompressedDir, normal);
        bool fromInside = cosTheta < 0.0;

        float prevIor = 1.0;
        if (fromInside)
        {
            // This is the first hit shader which means we determine previous IOR here
            prevIor = ior;
            
            normal *= -1.0;
            cosTheta *= -1.0;

            wavefrontRay.Throughput *= exp(-absorbance * hitInfo.T);
        }

        if (specularChance > 0.0) // adjust specular chance based on view angle
        {
            float newSpecularChance = mix(specularChance, 1.0, FresnelSchlick(cosTheta, prevIor, ior));
            float chanceMultiplier = (1.0 - newSpecularChance) / (1.0 - specularChance);
            transmissionChance *= chanceMultiplier;
            specularChance = newSpecularChance;
        }

        wavefrontRay.Radiance += emissive * wavefrontRay.Throughput;

        float rayProbability, newIor;
        bool newRayRefractive;
        uncompressedDir = BounceOffMaterial(uncompressedDir, specularChance, roughness, transmissionChance, ior, prevIor, normal, fromInside, rayProbability, newIor, newRayRefractive);
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

ivec2 ReorderInvocations(uint n)
{
    // Source: https://youtu.be/HgisCS30yAI
    // Info: https://developer.nvidia.com/blog/optimizing-compute-shaders-for-l2-locality-using-thread-group-id-swizzling/
    
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