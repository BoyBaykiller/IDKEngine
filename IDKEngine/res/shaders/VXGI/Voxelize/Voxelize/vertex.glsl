#version 460 core
#extension GL_ARB_bindless_texture : require

// 1 if NV_geometry_shader_passthrough and NV_viewport_swizzle are supported else 0
#define TAKE_FAST_GEOMETRY_SHADER_PATH AppInsert(TAKE_FAST_GEOMETRY_SHADER_PATH)

AppInclude(include/Compression.glsl)

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 3) in uint Normal;

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

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2, row_major) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

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

layout(std140, binding = 5) uniform VoxelizerDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} voxelizerDataUBO;

out InOutVars
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} outData;

#if !TAKE_FAST_GEOMETRY_SHADER_PATH
layout(location = 0) uniform int RenderAxis;
#endif

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance];

    mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
    mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);

    outData.FragPos = (modelMatrix * vec4(Position, 1.0)).xyz;

    vec3 normal = DecompressSR11G11B10(Normal);

    mat3 unitVecToWorld = mat3(transpose(invModelMatrix));
    outData.Normal = normalize(unitVecToWorld * normal);
    outData.TexCoord = TexCoord;

    outData.MaterialIndex = mesh.MaterialIndex;
    outData.EmissiveBias = mesh.EmissiveBias;

    gl_Position = voxelizerDataUBO.OrthoProjection * vec4(outData.FragPos, 1.0);

#if !TAKE_FAST_GEOMETRY_SHADER_PATH

    // Instead of doing a single draw call with a standard geometry shader to select the swizzle
    // we render the scene 3 times, each time with a different swizzle. I have observed this to be slightly faster
    if (RenderAxis == 0) gl_Position = gl_Position.zyxw;
    if (RenderAxis == 1) gl_Position = gl_Position.xzyw;
#endif
}
