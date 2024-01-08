#version 460 core
#extension GL_NV_mesh_shader : require
#extension GL_NV_gpu_shader5 : require
#extension GL_ARB_bindless_texture : require
#extension GL_KHR_shader_subgroup_ballot : require

AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)

#define MESHLETS_PER_WORKGROUP 32
layout(local_size_x = MESHLETS_PER_WORKGROUP) in;

struct DrawElementsCmd
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;

    uint BlasRootNodeIndex;
};

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    uint MeshletsStart;
    vec3 Absorbance;
    uint MeshletCount;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
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

layout(std430, binding = 0) restrict readonly buffer DrawElementsCmdSSBO
{
    DrawElementsCmd DrawCommands[];
} drawElementsCmdSSBO;

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 12) restrict readonly buffer MeshletSSBO
{
    Meshlet Meshlets[];
} meshletSSBO;

layout(std430, binding = 13) restrict readonly buffer MeshletInfoSSBO
{
    MeshletInfo MeshletsInfo[];
} meshletInfoSSBO;

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

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

taskNV out InOutVars
{
    // interstingly this needs to be passed down, otherwise we get subtle bugs
    uint MeshID;
    uint MeshletsStart;
    uint8_t SurvivingMeshlets[MESHLETS_PER_WORKGROUP];
} outData;

void main()
{
    uint meshID = gl_DrawID;
    Mesh mesh = meshSSBO.Meshes[meshID];
    if (gl_GlobalInvocationID.x >= mesh.MeshletCount)
    {
        return;
    }

    uint8_t localMeshlet = uint8_t(gl_LocalInvocationIndex);
    uint workgroupFirstMeshlet = mesh.MeshletsStart + (gl_WorkGroupID.x * MESHLETS_PER_WORKGROUP);
    uint workgroupThisMeshlet = workgroupFirstMeshlet + localMeshlet;

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance];
    MeshletInfo meshletInfo = meshletInfoSSBO.MeshletsInfo[workgroupThisMeshlet];
    
    bool isVisible = true;

    Box meshletLocalBounds = Box(meshletInfo.Min, meshletInfo.Max);
    
    // // Subpixel culling
    // {
    //     bool vertexBehindFrustum;
    //     Box meshletNdcBounds = BoxTransformPerspective(meshletLocalBounds, basicDataUBO.ProjView * meshInstance.ModelMatrix, vertexBehindFrustum);
    //     vec2 renderSize = textureSize(gBufferDataUBO.AlbedoAlpha, 0);
    //     vec3 uvMin = vec3(meshletNdcBounds.Min.xy * 0.5 + 0.5, meshletNdcBounds.Min.z);
    //     vec3 uvMax = vec3(meshletNdcBounds.Max.xy * 0.5 + 0.5, meshletNdcBounds.Max.z);
    //     ivec2 size = ivec2(ceil((uvMax.xy - uvMin.xy) * renderSize));
    //     if (size.x < 1 || size.y < 1)
    //     {
    //         isVisible = false;
    //     }
    // }

    // Frustum Culling
    if (isVisible)
    {
        Frustum frustum = GetFrustum(basicDataUBO.ProjView * meshInstance.ModelMatrix);
        isVisible = FrustumBoxIntersect(frustum, meshletLocalBounds);
    }

    // Hi-Z Occlusion Culling
    bool vertexBehindFrustum;
    Box meshletOldNdcBounds = BoxTransformPerspective(meshletLocalBounds, basicDataUBO.PrevProjView * meshInstance.PrevModelMatrix, vertexBehindFrustum);
    if (isVisible && !vertexBehindFrustum)
    {
        isVisible = BoxDepthBufferIntersect(meshletOldNdcBounds, gBufferDataUBO.Depth);
    }

    uvec4 visibleMeshletsBitmask = subgroupBallot(isVisible);
    if (isVisible)
    {
        uint offset = subgroupBallotExclusiveBitCount(visibleMeshletsBitmask);
        outData.SurvivingMeshlets[offset] = localMeshlet;
    }

    if (gl_LocalInvocationIndex == 0)
    {
        outData.MeshletsStart = workgroupFirstMeshlet;
        outData.MeshID = meshID;
        
        uint survivingCount = subgroupBallotBitCount(visibleMeshletsBitmask);
        gl_TaskCountNV = survivingCount;
    }
}