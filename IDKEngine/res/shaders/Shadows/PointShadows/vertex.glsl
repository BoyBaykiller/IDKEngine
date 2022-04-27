#version 460 core
#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_AMD_vertex_shader_layer : enable
#extension GL_NV_viewport_array : enable
#extension GL_NV_viewport_array2 : enable

#define IS_VERTEX_LAYERED_RENDERING (GL_ARB_shader_viewport_layer_array || GL_AMD_vertex_shader_layer || GL_NV_viewport_array || GL_NV_viewport_array2)

layout(location = 0) in vec3 Position;

struct PointShadow
{
    samplerCubeShadow Sampler;
    float NearPlane;
    float FarPlane;

    mat4 ProjViewMatrices[6];

    vec3 _pad0;
    int LightIndex;
};

struct Mesh
{
    int InstanceCount;
    int MatrixStart;
    int NodeStart;
    int BLASDepth;
    int MaterialIndex;
    float Emissive;
    float NormalMapStrength;
    float SpecularChance;
    float Roughness;
    float RefractionChance;
};

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 4) restrict readonly buffer MatrixSSBO
{
    mat4 Models[];
} matrixSSBO;

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[64];
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

    // gl_BaseInstance is a specific manipulated value from the culling shadowCompute shader
    // It contains 3 bit values, six at maximum, which represent the faces each instance of a mesh is visible on
    const int MAX = 2 * 2 * 2 - 1;
    gl_Layer = bitfieldExtract(gl_BaseInstance, 3 * gl_InstanceID, 3) & MAX;
    
    const int glInstanceID = 0; // TODO: Work out actual instanceID value
    mat4 model = matrixSSBO.Models[meshSSBO.Meshes[gl_DrawID].MatrixStart + glInstanceID];
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[gl_Layer] * vec4(outData.FragPos, 1.0);

#else

    // In multi pass shadows the layer is simply passed as a uniform before each pass

    mat4 model = matrixSSBO.Models[meshSSBO.Meshes[gl_DrawID].MatrixStart + gl_InstanceID];
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[Layer] * vec4(outData.FragPos, 1.0);

#endif
}