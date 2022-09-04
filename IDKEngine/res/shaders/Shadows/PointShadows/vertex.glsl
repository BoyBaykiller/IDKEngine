#version 460 core
#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_AMD_vertex_shader_layer : enable
#extension GL_NV_viewport_array2 : enable

#define IS_VERTEX_LAYERED_RENDERING (GL_ARB_shader_viewport_layer_array || GL_AMD_vertex_shader_layer || GL_NV_viewport_array2)

layout(location = 0) in vec3 Position;

struct PointShadow
{
    samplerCube Sampler;
    samplerCubeShadow SamplerShadow;
    
    mat4 ProjViewMatrices[6];

    float NearPlane;
    float FarPlane;
    int LightIndex;
    float _pad0;
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

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 4) restrict readonly buffer MatrixSSBO
{
    mat4 Models[];
} matrixSSBO;

layout(std140, binding = 1) uniform ShadowDataUBO
{
    #define GLSL_MAX_UBO_POINT_SHADOW_COUNT 16 // used in shader and client code - keep in sync!
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

out InOutVars
{
    vec3 FragPos;
} outData;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int Layer;

void main()
{
#if IS_VERTEX_LAYERED_RENDERING

    // visibleCubemapFacesInfo is a specific manipulated value from the culling shadowCompute shader
    // It contains 3 bit values, six at maximum, which represent the faces each instance of a mesh is visible on
    int visibleCubemapFacesInfo = meshSSBO.Meshes[gl_DrawID].VisibleCubemapFacesInfo;

    const int MAX = 2 * 2 * 2 - 1;
    gl_Layer = bitfieldExtract(visibleCubemapFacesInfo, 3 * gl_InstanceID, 3) & MAX;
    
    const uint glInstanceID = 0;  // TODO: Work out actual instanceID value
    mat4 model = matrixSSBO.Models[gl_BaseInstance + glInstanceID];
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[gl_Layer] * vec4(outData.FragPos, 1.0);

#else

    // In multi pass shadows the layer is simply passed as a uniform before each pass

    mat4 model = matrixSSBO.Models[gl_BaseInstance + gl_InstanceID];
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[Layer] * vec4(outData.FragPos, 1.0);

#endif
}