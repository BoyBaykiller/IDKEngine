AppInclude(include/GpuTypes.glsl)

// These constants are used in shader and client code. Keep in sync!
#define GPU_MAX_UBO_POINT_SHADOW_COUNT 128
#define GPU_MAX_UBO_LIGHT_COUNT 256

layout(std140, binding = 0) uniform PerFrameDataUBO
{
    GpuPerFrameData perFrameDataUBO;
};

layout(std140, binding = 1) uniform LightsUBO
{
    GpuLight Lights[GPU_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(std140, binding = 2) uniform ShadowsUBO
{
    GpuPointShadow PointShadows[GPU_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowsUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int SampleCount;
    float MipmapBias;
    int TemporalAntiAliasingMode;
} taaDataUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

layout(std140, binding = 5) uniform VoxelizerDataUBO
{
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} voxelizerDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D Normal;
    sampler2D MetallicRoughness;
    sampler2D Emissive;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;