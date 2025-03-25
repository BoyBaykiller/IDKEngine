#version 460 core

AppInclude(include/Surface.glsl)
AppInclude(include/Sampling.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(VXGI/ConeTraceGI/include/Impl.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxels;

layout(std140, binding = 0) uniform SettingsUBO
{
    ConeTraceGISettings ConeTraceSettings;
} settingsUBO;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 1.0)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }

    vec3 fragPos = PerspectiveTransformUvDepth(vec3(uv, depth), perFrameDataUBO.InvProjView);
    vec3 normal = DecodeUnitVec(texelFetch(gBufferDataUBO.Normal, imgCoord, 0).rg);
    float specular = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).r;
    float roughness = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).g;

    Surface surface = GetDefaultSurface();
    surface.Metallic = specular;
    surface.Roughness = roughness;
    surface.Normal = normal;

    vec3 viewDir = fragPos - perFrameDataUBO.ViewPos;
    vec3 indirectLight = IndirectLight(surface, SamplerVoxels, fragPos, viewDir, settingsUBO.ConeTraceSettings);

    imageStore(ImgResult, imgCoord, vec4(indirectLight, 1.0));
}
