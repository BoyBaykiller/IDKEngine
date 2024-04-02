#version 460 core

AppInclude(include/Random.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;

layout(std140, binding = 7) uniform SettingsUBO
{
    int SampleCount;
    float Radius;
    float Strength;
} settingsUBO;

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
    vec3 normal = normalize(texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).rgb);
    vec3 fragPos = PerspectiveTransformUvDepth(vec3(uv, depth), perFrameDataUBO.InvProjView);

    float occlusion = SSAO(fragPos, normal);

    imageStore(ImgResult, imgCoord, vec4(occlusion));
}

float SSAO(vec3 fragPos, vec3 normal)
{
    fragPos += normal * 0.04;

    float occlusion = 0.0;

    bool taaEnabled = taaDataUBO.TemporalAntiAliasingMode != TEMPORAL_ANTI_ALIASING_MODE_NO_AA;
    uint noiseIndex = taaEnabled ? (perFrameDataUBO.Frame % taaDataUBO.SampleCount) * (settingsUBO.SampleCount * 3) : 0u;
    for (int i = 0; i < settingsUBO.SampleCount; i++)
    {
        float rnd0 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex++);
        float rnd1 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex++);
        float rnd2 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex++);

        vec3 samplePos = fragPos + CosineSampleHemisphere(normal, rnd0, rnd1) * settingsUBO.Radius * rnd2;
        
        vec3 projectedSample = PerspectiveTransform(samplePos, perFrameDataUBO.ProjView);
        projectedSample.xy = projectedSample.xy * 0.5 + 0.5;

        float depth = texture(gBufferDataUBO.Depth, projectedSample.xy).r;
        if (projectedSample.z > depth)
        {
            vec3 sampleToFrag = fragPos - samplePos;
            float weight = dot(sampleToFrag, sampleToFrag) / (settingsUBO.Radius * settingsUBO.Radius);
            occlusion += weight;
        }
    }
    occlusion /= float(settingsUBO.SampleCount);
    occlusion *= settingsUBO.Strength;

   return occlusion;
}
