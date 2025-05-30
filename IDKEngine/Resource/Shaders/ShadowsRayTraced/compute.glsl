#version 460 core

AppInclude(include/Sampling.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

#define LOCAL_SIZE_X 8
#define LOCAL_SIZE_Y 8
#define TRAVERSAL_STACK_USE_SHARED_STACK_SIZE LOCAL_SIZE_X * LOCAL_SIZE_Y
AppInclude(include/BVHIntersect.glsl)

layout(local_size_x = LOCAL_SIZE_X, local_size_y = LOCAL_SIZE_Y, local_size_z = 1) in;

layout(location = 0) uniform int RayTracingSamples;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    
    bool taaEnabled = taaDataUBO.TemporalAntiAliasingMode != ENUM_ANTI_ALIASING_MODE_NONE;
    uint noiseIndex = taaEnabled ? (perFrameDataUBO.Frame % taaDataUBO.SampleCount) * RayTracingSamples : 0u;
    // InitializeRandomSeed((imgCoord.y * 4096 + imgCoord.x) * (noiseIndex + 1));

    GpuPointShadow pointShadow = shadowsUBO.PointShadows[gl_GlobalInvocationID.z];

    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 1.0 || any(greaterThanEqual(imgCoord, imageSize(image2D(pointShadow.RayTracedShadowMapImage)))))
    {
        return;
    }

    GpuLight light = lightsUBO.Lights[pointShadow.LightIndex]; 

    vec2 uv = (imgCoord + 0.5) / imageSize(image2D(pointShadow.RayTracedShadowMapImage));
    vec3 ndc = vec3(uv * 2.0 - 1.0, depth);
    vec3 unjitteredFragPos = PerspectiveTransform(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), perFrameDataUBO.InvProjView);
    vec3 normal = DecodeUnitVec(texelFetch(gBufferDataUBO.Normal, imgCoord, 0).rg);

    vec3 sampleToLightDir = light.Position - unjitteredFragPos;
    float cosTheta = dot(normal, sampleToLightDir);
    if (cosTheta <= 0.0)
    {
        imageStore(image2D(pointShadow.RayTracedShadowMapImage), imgCoord, vec4(0.0));
        return;
    }
    
    float shadow = 0.0;
    for (int i = 0; i < RayTracingSamples; i++)
    {
        vec3 biasedPosition = unjitteredFragPos + normal * 0.01;

        float rnd0 = InterleavedGradientNoise(imgCoord, noiseIndex + 0); // GetRandomFloat01()
        float rnd1 = InterleavedGradientNoise(imgCoord, noiseIndex + 1); // GetRandomFloat01()
        noiseIndex++;

        vec3 fragToLight = light.Position - biasedPosition;
        float distanceToLight, pdf;
        vec3 direction = SampleSphere(fragToLight, light.Radius, rnd0, rnd1, distanceToLight, pdf);

        Ray ray;
        ray.Origin = biasedPosition;
        ray.Direction = direction;

        HitInfo hitInfo;
        if (TraceRayAny(ray, hitInfo, true, distanceToLight - 0.001))
        {
            shadow += 1.0;
        }
    }
    shadow /= RayTracingSamples;

    imageStore(image2D(pointShadow.RayTracedShadowMapImage), imgCoord, vec4(shadow));
}