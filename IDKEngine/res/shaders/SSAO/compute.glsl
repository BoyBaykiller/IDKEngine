#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Random.glsl)
AppInclude(include/Transformations.glsl)

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
    float DeltaRenderTime;
    float Time;
} basicDataUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    float MipmapBias;
    int TemporalAntiAliasingMode;
} taaDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

uniform int Samples;
uniform float Radius;
uniform float Strength;

float SSAO(vec3 fragPos, vec3 normal);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 1.0)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }

    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);
    vec3 normal = texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).rgb;
    vec3 fragPos = PerspectiveTransformUvDepth(vec3(uv, depth), basicDataUBO.InvProjView);

    float occlusion = SSAO(fragPos, normal);

    imageStore(ImgResult, imgCoord, vec4(occlusion));
}

float SSAO(vec3 fragPos, vec3 normal)
{
    fragPos += normal * 0.04;

    float occlusion = 0.0;

    bool taaEnabled = taaDataUBO.TemporalAntiAliasingMode != TEMPORAL_ANTI_ALIASING_MODE_NO_AA;
    uint noiseIndex = taaEnabled ? (basicDataUBO.Frame % taaDataUBO.Samples) * (Samples * 3) : 0u;
    for (int i = 0; i < Samples; i++)
    {
        float rnd0 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex++);
        float rnd1 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex++);
        float rnd2 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex++);

        vec3 samplePos = fragPos + CosineSampleHemisphere(normal, rnd0, rnd1) * Radius * rnd2;
        
        vec3 projectedSample = PerspectiveTransform(samplePos, basicDataUBO.ProjView);
        projectedSample.xy = projectedSample.xy * 0.5 + 0.5;

        float depth = texture(gBufferDataUBO.Depth, projectedSample.xy).r;
        if (projectedSample.z > depth)
        {
            vec3 sampleToFrag = fragPos - samplePos;
            float weight = dot(sampleToFrag, sampleToFrag) / (Radius * Radius);
            occlusion += weight;
        }
    }
    occlusion /= float(Samples);
    occlusion *= Strength;

   return occlusion;
}
