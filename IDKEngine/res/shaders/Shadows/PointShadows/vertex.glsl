#version 460 core
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_NV_viewport_array2 : enable
#extension GL_AMD_vertex_shader_layer : enable

#define HAS_VERTEX_LAYERED_RENDERING (GL_ARB_shader_viewport_layer_array || GL_NV_viewport_array2 || GL_AMD_vertex_shader_layer)

layout(location = 0) in vec3 Position;

AppInclude(shaders/include/Buffers.glsl)

out InOutVars
{
    vec3 FragPos;
} outData;

layout(location = 0) uniform int ShadowIndex;
layout(location = 1) uniform int Layer;

void main()
{
#if HAS_VERTEX_LAYERED_RENDERING

    // CubemapShadowCullInfo is a specific manipulated value from the culling compute shader
    // It contains 3 bit values, six at maximum, which represent the faces each instance of a mesh is visible on
    uint cubemapShadowCullInfo = meshSSBO.Meshes[gl_DrawID].CubemapShadowCullInfo;

    gl_Layer = int(bitfieldExtract(cubemapShadowCullInfo, 3 * gl_InstanceID, 3));
    
    const uint glInstanceID = 0;  // TODO: Work out actual instanceID value
    mat4 model = meshInstanceSSBO.MeshInstances[gl_BaseInstance + glInstanceID].ModelMatrix;
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[gl_Layer] * vec4(outData.FragPos, 1.0);

#else

    // In multi pass shadows the layer is simply passed as a uniform before each pass

    mat4 model = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance].ModelMatrix;
    outData.FragPos = vec3(model * vec4(Position, 1.0));
    gl_Position = shadowDataUBO.PointShadows[ShadowIndex].ProjViewMatrices[Layer] * vec4(outData.FragPos, 1.0);

#endif
}