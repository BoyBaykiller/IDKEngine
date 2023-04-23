struct Vertex
{
    vec3 Position;
    float _pad0;

    vec2 TexCoord;
    uint Tangent;
    uint Normal;
};

struct DrawCommand
{
    uint Count;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};
layout(std430, binding = 0) restrict readonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandSSBO;

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
layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};
layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

#extension GL_ARB_bindless_texture : require
#extension GL_AMD_gpu_shader_half_float : enable
#extension GL_AMD_gpu_shader_half_float_fetch : enable // requires GL_AMD_gpu_shader_half_float
#if defined GL_AMD_gpu_shader_half_float_fetch
#define HF_SAMPLER_2D f16sampler2D
#else
#define HF_SAMPLER_2D sampler2D
#endif

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
layout(std430, binding = 3) restrict readonly buffer MaterialSSBO
{
    Material Materials[];
} materialSSBO;

struct Node
{
    vec3 Min;
    uint TriStartOrLeftChild;
    vec3 Max;
    uint TriCount;
};
layout(std430, binding = 4) restrict readonly buffer BlasSSBO
{
    Node Nodes[];
} blasSSBO;

struct Triangle
{
    Vertex Vertex0;
    Vertex Vertex1;
    Vertex Vertex2;
};
layout(std430, binding = 5) restrict readonly buffer BlasTriangleSSBO
{
    Triangle Triangles[];
} blasTriangleSSBO;

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

struct DispatchCommand
{
    uint NumGroupsX;
    uint NumGroupsY;
    uint NumGroupsZ;
};
layout(std430, binding = 8) restrict buffer DispatchCommandSSBO
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
layout(std140, binding = 1) uniform ShadowDataUBO
{
    #define GLSL_MAX_UBO_POINT_SHADOW_COUNT 16 // used in shader and client code - keep in sync!
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
} shadowDataUBO;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    int PointShadowIndex;
};
layout(std140, binding = 2) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    #define GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT 36 // used in shader and client code - keep in sync!
    vec4 Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT / 2];
    int Samples;
    int Enabled;
    uint Frame;
    float VelScale;
} taaDataUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

layout(std140, binding = 5) uniform VoxelizerDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} voxelizerDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;