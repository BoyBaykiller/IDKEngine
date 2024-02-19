#version 460 core
#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_AMD_vertex_shader_layer : enable
#extension GL_NV_viewport_array2 : enable
#define HAS_VERTEX_LAYERED_RENDERING (GL_ARB_shader_viewport_layer_array || GL_AMD_vertex_shader_layer || GL_NV_viewport_array2)

#if !HAS_VERTEX_LAYERED_RENDERING
#error "PointShadow vertex shader shader is missing extension support."
#endif

AppInclude(include/Constants.glsl)

layout(location = 0) in vec3 Position;

struct MeshInstance
{
    mat4x3 ModelMatrix;
    mat4x3 InvModelMatrix;
    mat4x3 PrevModelMatrix;
    vec3 _pad0;
    uint MeshIndex;
};

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

layout(std430, binding = 2, row_major) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 3) restrict buffer VisibleMeshInstanceSSBO
{
    uint MeshInstanceIDs[];
} visibleMeshInstanceSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

layout(location = 0) uniform int ShadowIndex;

void main()
{
    uint faceAndMeshInstanceID = visibleMeshInstanceSSBO.MeshInstanceIDs[gl_InstanceID + gl_BaseInstance * 6];
    uint faceID = faceAndMeshInstanceID >> 29;
    uint meshInstanceID = faceAndMeshInstanceID & ((1u << 29) - 1);
    
    mat4 modelMatrix = mat4(meshInstanceSSBO.MeshInstances[meshInstanceID].ModelMatrix);
    vec3 fragPos = vec3(modelMatrix * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[faceID] * vec4(fragPos, 1.0);
    gl_Layer = int(faceID);
}