#version 460 core
#extension GL_ARB_bindless_texture : require

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerModulatedIrradiance;
layout(binding = 1) uniform sampler2D SamplerSSUnmodulatedIrradiance;
layout(binding = 2) uniform sampler2D SamplerVolumetricLighting;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);
    
    vec3 albedo = texture(gBufferDataUBO.AlbedoAlpha, uv).rgb;
    vec3 volumetricLighting = texture(SamplerVolumetricLighting, uv).rgb;

    vec3 modulatedIrradiance = texture(SamplerModulatedIrradiance, uv).rgb;
    vec3 ssIndirectLight = texture(SamplerSSUnmodulatedIrradiance, uv).rgb * albedo;

    vec3 color = modulatedIrradiance + ssIndirectLight + volumetricLighting;
    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}
