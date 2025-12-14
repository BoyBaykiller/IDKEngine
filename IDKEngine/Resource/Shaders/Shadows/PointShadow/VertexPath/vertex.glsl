#version 460 core
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_AMD_vertex_shader_layer : enable
#extension GL_NV_viewport_array2 : enable
#define HAS_VERTEX_LAYERED_RENDERING (GL_ARB_shader_viewport_layer_array || GL_AMD_vertex_shader_layer || GL_NV_viewport_array2)

#if !HAS_VERTEX_LAYERED_RENDERING
#error "PointShadow vertex shader shader is missing extension support."
#endif

AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

layout(location = 0) uniform int ShadowIndex;

void main()
{
    vec3 vertexPosition = Unpack(vertexPositionsSSBO.Positions[gl_VertexID]);

    uint faceAndMeshInstanceId = visibleMeshInstanceIdSSBO.Ids[gl_BaseInstance * 6 + gl_InstanceID];
    uint faceId = faceAndMeshInstanceId >> 29;
    uint meshInstanceId = faceAndMeshInstanceId & ((1u << 29) - 1);
    
    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceId];
    GpuMeshTransform meshTransform = meshTransformSSBO.Transforms[meshInstance.MeshTransformId];

    mat4 modelMatrix = mat4(meshTransform.ModelMatrix);
    vec3 fragPos = vec3(modelMatrix * vec4(vertexPosition, 1.0));
    gl_Position = shadowsUBO.PointShadows[ShadowIndex].ProjViewMatrices[faceId] * vec4(fragPos, 1.0);
    gl_Layer = int(faceId);
}