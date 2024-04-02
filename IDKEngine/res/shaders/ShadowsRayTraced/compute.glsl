#version 460 core

#define DECLARE_BVH_TRAVERSAL_STORAGE_BUFFERS
AppInclude(include/StaticStorageBuffers.glsl)

AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Random.glsl)
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
    
    PointShadow pointShadow = shadowsUBO.PointShadows[gl_GlobalInvocationID.z];

    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 0.0 || any(greaterThanEqual(imgCoord, imageSize(image2D(pointShadow.RayTracedShadowMapImage)))))
    {
        return;
    }

    Light light = lightsUBO.Lights[pointShadow.LightIndex]; 

    vec2 uv = (imgCoord + 0.5) / imageSize(image2D(pointShadow.RayTracedShadowMapImage));
    vec3 ndc = vec3(uv * 2.0 - 1.0, depth);
    vec3 unjitteredFragPos = PerspectiveTransform(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), perFrameDataUBO.InvProjView);
    vec3 normal = normalize(texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).rgb);

    vec3 sampleToLightDir = light.Position - unjitteredFragPos;
    float cosTerm = dot(normal, sampleToLightDir);
    if (cosTerm <= 0.0)
    {
        imageStore(image2D(pointShadow.RayTracedShadowMapImage), imgCoord, vec4(0.0));
        return;
    }

    bool taaEnabled = taaDataUBO.TemporalAntiAliasingMode != TEMPORAL_ANTI_ALIASING_MODE_NO_AA;
    uint noiseIndex = taaEnabled ? (perFrameDataUBO.Frame % taaDataUBO.SampleCount) * RayTracingSamples : 0u;
    float shadow = 0.0;
    for (int j = 0; j < RayTracingSamples; j++)
    {
        vec3 biasedPosition = unjitteredFragPos + normal * 0.01;

        float rnd0 = InterleavedGradientNoise(imgCoord, noiseIndex + 0);
        float rnd1 = InterleavedGradientNoise(imgCoord, noiseIndex + 1);
        noiseIndex++;

        vec3 fragToLight = light.Position - biasedPosition;
        float distanceToLight, pdf;
        vec3 direction = SampleLight(fragToLight, light.Radius, rnd0, rnd1, distanceToLight, pdf);

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