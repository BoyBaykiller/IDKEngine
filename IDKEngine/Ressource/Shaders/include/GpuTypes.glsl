struct PackedVec3
{
    float x, y, z;
};
vec3 Unpack(PackedVec3 floats)
{
    return vec3(floats.x, floats.y, floats.z);
}

struct PackedUVec3
{
    uint x, y, z;
};
uvec3 Unpack(PackedUVec3 uints)
{
    return uvec3(uints.x, uints.y, uints.z);
}

struct DrawElementsCmd
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct DispatchCommand
{
    int NumGroupsX;
    int NumGroupsY;
    int NumGroupsZ;
};

struct PerFrameData
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
    samplerCube ShadowMapTexture;
    samplerCubeShadow PcfShadowTexture;

    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    // We don't store image2D itself but instead uvec2
    // and cast later because of NVIDIA driver bug: https://forums.developer.nvidia.com/t/driver-bug-bindless-image2d-in-std140-buffer/287957
    uvec2 RayTracedShadowMapImage;
    float FarPlane;
    int LightIndex;
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

struct WavefrontRay
{
    vec3 Origin;
    float PreviousIOROrDebugNodeCounter;

    vec3 Throughput;
    float CompressedDirectionX;

    vec3 Radiance;
    float CompressedDirectionY;
};

// Allow for custum sampler2D types. Mainly for supporting fp16 samplers from GL_AMD_gpu_shader_half_float_fetch
#ifndef MATERIAL_SAMPLER_2D_TYPE
#define MATERIAL_SAMPLER_2D_TYPE sampler2D
#endif
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

    MATERIAL_SAMPLER_2D_TYPE BaseColor;
    MATERIAL_SAMPLER_2D_TYPE MetallicRoughness;

    MATERIAL_SAMPLER_2D_TYPE Normal;
    MATERIAL_SAMPLER_2D_TYPE Emissive;

    MATERIAL_SAMPLER_2D_TYPE Transmission;
    bool DoAlphaBlending;
    uint _pad0;
};

struct Vertex
{
    vec2 TexCoord;
    uint Tangent;
    uint Normal;
};

#ifdef DECLARE_MESHLET_RENDERING_TYPES
struct MeshletTaskCmd
{
    uint Count;
    uint First;
};

struct Meshlet
{
    uint VertexOffset;
    uint IndicesOffset;

    uint8_t VertexCount;
    uint8_t TriangleCount;
};

struct MeshletInfo
{
    vec3 Min;
    float _pad0;

    vec3 Max;
    float _pad1;
};
#endif
