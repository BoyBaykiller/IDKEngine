#version 460 core
#define PI 3.14159265
#define EPSILON 0.001
#extension GL_ARB_bindless_texture : require

AppInclude(include/Random.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    uint Frame;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

float SSAO(vec3 fragPos, vec3 normal);
vec3 ViewToNDC(vec3 ndc);
vec3 NDCToView(vec3 ndc);

uniform int Samples;
uniform float Radius;
uniform float Strength;

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
    InitializeRandomSeed(imgCoord.y * 4096 + imgCoord.x);

    vec3 normal = texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).rgb;

    vec3 fragPos = NDCToView(vec3(uv, depth) * 2.0 - 1.0);
    mat3 normalToView = mat3(transpose(basicDataUBO.InvView));
    normal = normalize(normalToView * normal);

    float occlusion = SSAO(fragPos, normal);

    imageStore(ImgResult, imgCoord, vec4(vec3(occlusion), 1.0));
}

float SSAO(vec3 fragPos, vec3 normal)
{
    fragPos += normal * 0.01;

    float occlusion = 0.0;
    float samples = Samples;
    for (int i = 0; i < Samples; i++)
    {
        float progress = i / float(Samples);
        vec3 samplePos = fragPos + CosineSampleHemisphere(normal, progress, GetRandomFloat01()) * Radius * mix(0.1, 1.0, progress * progress);
        
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

