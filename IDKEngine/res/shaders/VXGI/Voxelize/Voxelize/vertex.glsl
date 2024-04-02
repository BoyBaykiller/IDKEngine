#version 460 core

// 1 if NV_geometry_shader_passthrough and NV_viewport_swizzle are supported else 0
#define TAKE_FAST_GEOMETRY_SHADER_PATH AppInsert(TAKE_FAST_GEOMETRY_SHADER_PATH)

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Compression.glsl)

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 3) in uint Normal;

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
