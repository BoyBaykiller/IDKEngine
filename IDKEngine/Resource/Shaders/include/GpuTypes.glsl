struct PackedVec4
{
    float x, y, z, w;
};

struct PackedUVec4
{
    uint x, y, z, w;
};

struct PackedVec3
{
    float x, y, z;
};

struct PackedUVec3
{
    uint x, y, z;
};

struct PackedVec2
{
    float x, y;
};

vec4 Unpack(PackedVec4 floats)
{
    return vec4(floats.x, floats.y, floats.z, floats.w);
}
uvec4 Unpack(PackedUVec4 uints)
{
    return uvec4(uints.x, uints.y, uints.z, uints.w);
}

PackedVec3 Pack(vec3 floats)
{
    return PackedVec3(floats.x, floats.y, floats.z);
}
vec3 Unpack(PackedVec3 floats)
{
    return vec3(floats.x, floats.y, floats.z);
}

PackedUVec3 Pack(uvec3 uints)
{
    return PackedUVec3(uints.x, uints.y, uints.z);
}
uvec3 Unpack(PackedUVec3 uints)
{
    return uvec3(uints.x, uints.y, uints.z);
}

vec2 Unpack(PackedVec2 floats)
{
    return vec2(floats.x, floats.y);
}

struct GpuDrawElementsCmd
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct GpuDispatchCommand
{
    int NumGroupsX;
    int NumGroupsY;
    int NumGroupsZ;
};

struct GpuPerFrameData
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

struct GpuLight
{
    vec3 Position;
    float Radius;
    
    vec3 Color;
    int PointShadowIndex; // -1 for no shadow
    
    vec3 PrevPosition;
    float _pad0;
};

struct GpuPointShadow
{
    samplerCube ShadowMapTexture;
    samplerCubeShadow PcfShadowTexture;

    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    // We don't store image2D itself but instead uvec2
    // and cast later because of NVIDIA driver bug: https://forums.developer.nvidia.com/t/driver-bug-bindless-image2d-in-std140-buffer/287957
    // TODO: Apparently this is fixed now, revert the hack once I can confirm
    uvec2 RayTracedShadowMapImage;
    float FarPlane;
    int LightIndex;
};

struct GpuMesh
{
    vec3 LocalBoundsMin;
    int MaterialId;

    vec3 LocalBoundsMax;
    float NormalMapStrength;

    vec3 AbsorbanceBias;
    uint MeshletsOffset;

    uint MeshletCount;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;

    float TransmissionBias;
    float IORBias;
    uint InstanceCount;
    uint VertexCount;

    vec3 _pad0;
    bool TintOnTransmissive;
};

struct GpuMeshInstance
{
    uint MeshId;
    uint MeshTransformId;
};

struct GpuMeshTransform
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
};

struct GpuBlasTriangle
{
    uvec3 Indices;
    uint GeometryId;
};

struct GpuBlasDesc
{
    int NodeOffset;
    int NodeCount;
    int TriangleOffset;
    int TriangleCount;
    int LeafIndicesOffset;
    int LeafIndicesCount;
    int ParentIndicesOffset;
    int ParentIndicesCount;
    int RequiredStackSize;
    bool IsRefittable;
};

struct GpuBlasInstance
{
    uint BlasId;
    uint MeshTransformId;
};

struct GpuBlasNode
{
    vec3 Min;
    uint TriStartOrChild;
    vec3 Max;
    uint TriCount;
};

struct GpuTlasNode
{
    vec3 Min;
    uint IsLeafAndChildOrInstanceId;
    vec3 Max;
    float _pad0;
};

struct GpuWavefrontRay
{
    vec3 Origin;
    float PreviousIOROrTraverseCost;

    vec3 Throughput;
    float PackedDirectionX;

    vec3 Radiance;
    float PackedDirectionY;
};

// Allow for custum sampler2D types. Mainly for supporting fp16 samplers from GL_AMD_gpu_shader_half_float_fetch
#ifndef MATERIAL_SAMPLER_2D_TYPE
#define MATERIAL_SAMPLER_2D_TYPE sampler2D
#endif
struct GpuMaterial
{
    vec3 EmissiveFactor;
    uint BaseColorFactor;

    vec3 Absorbance;
    float IOR;

    float TransmissionFactor;
    float RoughnessFactor;
    float MetallicFactor;
    float AlphaCutoff;

    MATERIAL_SAMPLER_2D_TYPE BaseColor;
    MATERIAL_SAMPLER_2D_TYPE MetallicRoughness;

    MATERIAL_SAMPLER_2D_TYPE Normal;
    MATERIAL_SAMPLER_2D_TYPE Emissive;

    MATERIAL_SAMPLER_2D_TYPE Transmission;
    bool IsVolumetric;
    float _pad0;
};

struct GpuVertex
{
    PackedVec2 TexCoord;
    uint Tangent;
    uint Normal;
};

#ifdef DECLARE_MESHLET_RENDERING_TYPES
struct GpuMeshletTaskCmd
{
    uint Count;
    uint First;
};

struct GpuMeshlet
{
    uint VertexOffset;
    uint IndicesOffset;

    uint8_t VertexCount;
    uint8_t TriangleCount;
};

struct GpuMeshletInfo
{
    vec3 Min;
    float _pad0;

    vec3 Max;
    float _pad1;
};
#endif

struct UnskinnedVertex
{
    PackedUVec4 JointIndices;
    PackedVec4 JointWeights;

    PackedVec3 Position;
    uint Tangent;
    uint Normal;
};
