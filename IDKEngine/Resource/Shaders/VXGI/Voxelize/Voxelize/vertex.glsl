#version 460 core

// 1 if NV_geometry_shader_passthrough and NV_viewport_swizzle are supported else 0
#define TAKE_FAST_GEOMETRY_SHADER_PATH AppInsert(TAKE_FAST_GEOMETRY_SHADER_PATH)

AppInclude(include/Compression.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

out InOutData
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialId;
    float EmissiveBias;
} outData;

#if !TAKE_FAST_GEOMETRY_SHADER_PATH
layout(location = 0) uniform int RenderAxis;
#endif

void main()
{
    GpuVertex vertex = vertexSSBO.Vertices[gl_VertexID];
    vec3 vertexPosition = Unpack(vertexPositionsSSBO.Positions[gl_VertexID]);

    GpuMesh mesh = meshSSBO.Meshes[gl_DrawID];
    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[gl_BaseInstance + gl_InstanceID];

    mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
    mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);

    outData.FragPos = (modelMatrix * vec4(vertexPosition, 1.0)).xyz;

    vec3 normal = DecompressSR11G11B10(vertex.Normal);

    mat3 unitVecToWorld = mat3(transpose(invModelMatrix));
    outData.Normal = normalize(unitVecToWorld * normal);
    outData.TexCoord = Unpack(vertex.TexCoord);

    outData.MaterialId = vertex.MaterialId;
    outData.EmissiveBias = mesh.EmissiveBias;

    vec3 ndc = MapToZeroOne(outData.FragPos, voxelizerDataUBO.GridMin, voxelizerDataUBO.GridMax) * 2.0 - 1.0;
    gl_Position = vec4(ndc, 1.0);

#if !TAKE_FAST_GEOMETRY_SHADER_PATH

    // Instead of doing a single draw call with a standard geometry shader to select the swizzle
    // we render the scene 3 times, each time with a different swizzle. I have observed this to be faster
    if (RenderAxis == 0) gl_Position = gl_Position.zyxw;
    if (RenderAxis == 1) gl_Position = gl_Position.xzyw;
#endif
}
