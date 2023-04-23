#version 460 core

// 1 if NV_geometry_shader_passthrough and NV_viewport_swizzle are supported else 0
#define TAKE_FAST_GEOMETRY_SHADER_PATH AppInsert(TAKE_FAST_GEOMETRY_SHADER_PATH)

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 3) in uint Normal;

layout(binding = 0, rgba16f) restrict uniform image3D ImgResult;

AppInclude(shaders/include/Buffers.glsl)

out InOutVars
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} outData;

vec3 DecompressSNorm32Fast(uint data);

#if !TAKE_FAST_GEOMETRY_SHADER_PATH
layout(location = 0) uniform int SwizzleAxis;
#endif

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance];

    outData.FragPos = (meshInstance.ModelMatrix * vec4(Position, 1.0)).xyz;

    vec3 normal = DecompressSNorm32Fast(Normal);

    mat3 normalToWorld = mat3(transpose(meshInstance.InvModelMatrix));
    outData.Normal = normalize(normalToWorld * normal);
    outData.TexCoord = TexCoord;

    outData.MaterialIndex = mesh.MaterialIndex;
    outData.EmissiveBias = mesh.EmissiveBias;

    gl_Position = voxelizerDataUBO.OrthoProjection * vec4(outData.FragPos, 1.0);
    
#if !TAKE_FAST_GEOMETRY_SHADER_PATH
    // Instead of doing a single draw call with a standard geometry shader to select the swizzle
    // we render the scene 3 times, each time with a different swizzle. I have observed this to be slightly faster
    if (SwizzleAxis == 0) gl_Position = gl_Position.zyxw;
    else if (SwizzleAxis == 1) gl_Position = gl_Position.xzyw;
#endif
}

vec3 DecompressSNorm32Fast(uint data)
{
    float r = (data >> 0) & ((1u << 11) - 1);
    float g = (data >> 11) & ((1u << 11) - 1);
    float b = (data >> 22) & ((1u << 10) - 1);

    r /= (1u << 11) - 1;
    g /= (1u << 11) - 1;
    b /= (1u << 10) - 1;

    return vec3(r, g, b) * 2.0 - 1.0;
}