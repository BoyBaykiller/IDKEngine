#version 460 core
#extension GL_ARB_bindless_texture : require

#define TAKE_VERTEX_LAYERED_RENDERING_PATH AppInsert(TAKE_VERTEX_LAYERED_RENDERING_PATH)

#if TAKE_VERTEX_LAYERED_RENDERING_PATH
    #extension GL_ARB_shader_viewport_layer_array : enable
    #extension GL_NV_viewport_array2 : enable
    #extension GL_AMD_vertex_shader_layer : enable

    #if !GL_ARB_shader_viewport_layer_array && !GL_NV_viewport_array2 && !GL_AMD_vertex_shader_layer
        #error "Cannot take the vertex layered rendering path as neither GL_ARB_shader_viewport_layer_array, GL_NV_viewport_array2 nor GL_AMD_vertex_shader_layer is supported" 
    #endif
#endif

AppInclude(include/Constants.glsl)

layout(location = 0) in vec3 Position;

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

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

layout(std430, binding = 1) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int Layer;

void main()
{
#if TAKE_VERTEX_LAYERED_RENDERING_PATH

    // CubemapShadowCullInfo is a specific manipulated value from the culling compute shader
    // It contains 3 bit values, six at maximum, which represent the faces each instance of a mesh is visible on
    uint cubemapShadowCullInfo = meshSSBO.Meshes[gl_DrawID].CubemapShadowCullInfo;

    gl_Layer = int(bitfieldExtract(cubemapShadowCullInfo, 3 * gl_InstanceID, 3));
    
    const uint glInstanceID = 0; // TODO: Work out actual instanceID value
    mat4 model = meshInstanceSSBO.MeshInstances[gl_BaseInstance + glInstanceID].ModelMatrix;
    vec3 fragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[gl_Layer] * vec4(fragPos, 1.0);

#else

    // In multi pass shadows the layer is simply passed as a uniform before each pass

    mat4 model = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance].ModelMatrix;
    vec3 fragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[Layer] * vec4(fragPos, 1.0);

#endif
}