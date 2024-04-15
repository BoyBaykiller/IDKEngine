#version 460 core
#extension GL_ARB_shader_viewport_layer_array : enable
#extension GL_AMD_vertex_shader_layer : enable
#extension GL_NV_viewport_array2 : enable
#define HAS_VERTEX_LAYERED_RENDERING (GL_ARB_shader_viewport_layer_array || GL_AMD_vertex_shader_layer || GL_NV_viewport_array2)

#if !HAS_VERTEX_LAYERED_RENDERING
#error "PointShadow vertex shader shader is missing extension support."
#endif

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Constants.glsl)

layout(location = 0) uniform int ShadowIndex;

void main()
{
    vec3 vertexPosition = Unpack(vertexPositionsSSBO.VertexPositions[gl_VertexID]);

    uint faceAndMeshInstanceID = visibleMeshInstanceSSBO.MeshInstanceIDs[gl_InstanceID + gl_BaseInstance * 6];
    uint faceID = faceAndMeshInstanceID >> 29;
    uint meshInstanceID = faceAndMeshInstanceID & ((1u << 29) - 1);
    
    mat4 modelMatrix = mat4(meshInstanceSSBO.MeshInstances[meshInstanceID].ModelMatrix);
    vec3 fragPos = vec3(modelMatrix * vec4(vertexPosition, 1.0));
    gl_Position = shadowsUBO.PointShadows[ShadowIndex].ProjViewMatrices[faceID] * vec4(fragPos, 1.0);
    gl_Layer = int(faceID);
}