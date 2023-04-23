#version 460 core
#define PI 3.14159265
#define EPSILON 0.001

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;

AppInclude(shaders/include/Buffers.glsl)

float SSAO(vec3 fragPos, vec3 normal);
vec3 ViewToNDC(vec3 ndc);
vec3 NDCToView(vec3 ndc);
vec3 CosineSampleHemisphere(float u, float v, vec3 normal);
float GetRandomFloat01();
uint GetPCGHash(inout uint seed);

uniform int Samples;
uniform float Radius;
uniform float Strength;

uint rngSeed;
void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    float depth = texture(gBufferDataUBO.Depth, uv).r;
    if (depth == 1.0)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }
    rngSeed = imgCoord.y * 4096 + imgCoord.x;

    vec3 normal = texture(gBufferDataUBO.NormalSpecular, uv).rgb;

    vec3 fragPos = NDCToView(vec3(uv, depth) * 2.0 - 1.0);
    mat3 normalToView = mat3(transpose(basicDataUBO.InvView));
    normal = normalize(normalToView * normal);

    float occlusion = SSAO(fragPos, normal);

    imageStore(ImgResult, imgCoord, vec4(vec3(occlusion), 1.0));
}

float SSAO(vec3 fragPos, vec3 normal)
{
    float occlusion = 0.0;
    float samples = Samples;
    for (int i = 0; i < Samples; i++)
    {
        float progress = i / float(Samples);
        vec3 samplePos = fragPos + CosineSampleHemisphere(GetRandomFloat01(), progress, normal) * Radius * mix(0.1, 1.0, progress * progress);
        
        vec3 projectedSample = ViewToNDC(samplePos) * 0.5 + 0.5;
        float depth = texture(gBufferDataUBO.Depth, projectedSample.xy).r;
    
        float weight = length(fragPos - samplePos) / Radius;
        occlusion += int(projectedSample.z >= depth) * weight;
    }
    occlusion /= samples;
    occlusion *= Strength;

   return occlusion;
}

vec3 ViewToNDC(vec3 viewPos)
{
    vec4 clipPos = basicDataUBO.Projection * vec4(viewPos, 1.0);
    return clipPos.xyz / clipPos.w;
}

vec3 NDCToView(vec3 ndc)
{
    vec4 viewPos = basicDataUBO.InvProjection * vec4(ndc, 1.0);
    return viewPos.xyz / viewPos.w;
}

// Source: https://blog.demofox.org/2020/05/25/casual-shadertoy-path-tracing-1-basic-camera-diffuse-emissive/
vec3 CosineSampleHemisphere(float u, float v, vec3 normal)
{
    float z = u * 2.0 - 1.0;
    float a = v * 2.0 * PI;
    float r = sqrt(1.0 - z * z);
    float x = r * cos(a);
    float y = r * sin(a);

    // Convert unit vector in sphere to a cosine weighted vector in hemisphere
    return normalize(normal + vec3(x, y, z));
}

float GetRandomFloat01()
{
    return float(GetPCGHash(rngSeed)) / 4294967296.0;
}

// Faster and much more random than Wang Hash
// See: https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/
uint GetPCGHash(inout uint seed)
{
    seed = seed * 747796405u + 2891336453u;
    uint word = ((seed >> ((seed >> 28u) + 4u)) ^ seed) * 277803737u;
    return (word >> 22u) ^ word;
}
