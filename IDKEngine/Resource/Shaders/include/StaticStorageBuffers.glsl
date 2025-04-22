#ifdef DECLARE_MESHLET_STORAGE_BUFFERS
    #define DECLARE_MESHLET_RENDERING_TYPES
#endif

AppInclude(include/GpuTypes.glsl)

// Binding 0 is reserved for temporary SSBOs

layout(std430, binding = 1) restrict buffer DrawElementsCmdSSBO
{
    GpuDrawElementsCmd Commands[];
} drawElementsCmdSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    GpuMesh Meshes[];
} meshSSBO;

layout(std430, binding = 3, row_major) restrict readonly buffer MeshInstanceSSBO
{
    GpuMeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 4) restrict buffer VisibleMeshInstanceIdSSBO
{
    uint Ids[];
} visibleMeshInstanceIdSSBO;

layout(std430, binding = 5) restrict readonly buffer MaterialSSBO
{
    GpuMaterial Materials[];
} materialSSBO;

layout(std430, binding = 6) restrict buffer VertexSSBO
{
    GpuVertex Vertices[];
} vertexSSBO;

layout(std430, binding = 7) restrict buffer VertexPositionsSSBO
{
    PackedVec3 Positions[];
} vertexPositionsSSBO;

#ifdef DECLARE_MESHLET_STORAGE_BUFFERS // Only used when mesh shader path is taken
layout(std430, binding = 8) restrict buffer MeshletTaskCmdSSBO
{
    GpuMeshletTaskCmd Commands[];
} meshletTaskCmdSSBO;

layout(std430, binding = 9) restrict buffer MeshletTasksCountSSBO
{
    uint Count;
} meshletTasksCountSSBO;

layout(std430, binding = 10) restrict readonly buffer MeshletSSBO
{
    GpuMeshlet Meshlets[];
} meshletSSBO;

layout(std430, binding = 11) restrict readonly buffer MeshletInfoSSBO
{
    GpuMeshletInfo MeshletsInfo[];
} meshletInfoSSBO;

layout(std430, binding = 12) restrict readonly buffer MeshletVertexIndicesSSBO
{
    uint VertexIndices[];
} meshletVertexIndicesSSBO;

layout(std430, binding = 13) restrict readonly buffer MeshletLocalIndicesSSBO
{
    uint PackedIndices[];
} meshletLocalIndicesSSBO;
#endif

layout(std430, binding = 14, row_major) restrict readonly buffer JointMatricesSSBO
{
    mat4x3 Matrices[];
} jointMatricesSSBO;

layout(std430, binding = 15) restrict buffer UnskinnedVertexSSBO
{
    UnskinnedVertex Vertices[];
} unskinnedVertexSSBO;

layout(std430, binding = 16) restrict buffer PrevVertexPositionSSBO
{
    PackedVec3 Positions[];
} prevVertexPositionSSBO;

layout(std430, binding = 20) restrict readonly buffer BlasDescSSBO
{
    GpuBlasDesc Descs[];
} blasDescSSBO;

layout(std430, binding = 21) restrict buffer BlasNodeSSBO
{
    GpuBlasNode Nodes[];
} blasNodeSSBO;

layout(std430, binding = 22) restrict readonly buffer BlasTriangleIndicesSSBO
{
    PackedUVec3 Indices[];
} blasTriangleIndicesSSBO;

layout(std430, binding = 23) restrict readonly buffer BlasParentIndicesSSBO
{
    int Indices[];
} blasParentIndicesSSBO;

layout(std430, binding = 24) restrict readonly buffer BlasLeafIndicesSSBO
{
    uint Indices[];
} blasLeafIndicesSSBO;

layout(std430, binding = 25) restrict buffer BlasRefitLocksSSBO
{
    uint Locks[];
} blasRefitLocksSSBO;

layout(std430, binding = 26) restrict readonly buffer TlasSSBO
{
    GpuTlasNode Nodes[];
} tlasSSBO;

layout(std430, binding = 30) restrict buffer WavefrontRaySSBO
{
    GpuWavefrontRay Rays[];
} wavefrontRaySSBO;

layout(std430, binding = 31) restrict buffer WavefrontPTSSBO
{
    GpuDispatchCommand DispatchCommand;
    uint Counts[2];
    uint PingPongIndex;
    uint AccumulatedSamples;
    uint AliveRayIndices[];
} wavefrontPTSSBO;
