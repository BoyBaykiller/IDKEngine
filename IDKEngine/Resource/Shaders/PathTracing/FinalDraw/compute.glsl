#version 460 core

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(PathTracing/include/Constants.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict uniform image2D ImgResult;
layout(binding = 1) restrict uniform image2D ImgAlbedo;
layout(binding = 2) restrict uniform image2D ImgNormal;

layout(std140, binding = 0) uniform SettingsUBO
{
    float FocalLength;
    float LenseRadius;
    bool DoDebugBVHTraversal;
    bool DoTraceLights;
    bool DoRussianRoulette;
} settingsUBO;

vec3 TurboColormap(float x);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;
    GpuWavefrontRay wavefrontRay = wavefrontRaySSBO.Rays[rayIndex];

    vec3 newResult = wavefrontRay.Radiance;
    if (settingsUBO.DoDebugBVHTraversal)
    {
        float x = wavefrontRay.PreviousIOROrTraverseCost / 150.0;
        vec3 col = TurboColormap(x);
        newResult = col;
    }

    vec3 lastFrameResult = imageLoad(ImgResult, imgCoord).rgb;
    newResult = mix(lastFrameResult, newResult, 1.0 / (float(wavefrontPTSSBO.AccumulatedSamples) + 1.0));
    imageStore(ImgResult, imgCoord, vec4(newResult, 1.0));

    GpuAOVRay aovRay = aovRaySSBO.Rays[rayIndex];
    vec3 lastFrameAlbedo = imageLoad(ImgAlbedo, imgCoord).rgb;
    vec3 newAlbedo = mix(lastFrameAlbedo, aovRay.Albedo, 1.0 / (float(wavefrontPTSSBO.AccumulatedSamples) + 1.0));
    imageStore(ImgAlbedo, imgCoord, vec4(newAlbedo, 1.0));

    vec3 lastFrameNormal = imageLoad(ImgNormal, imgCoord).rgb;
    vec3 newNormal = mix(lastFrameNormal, aovRay.Normal, 1.0 / (float(wavefrontPTSSBO.AccumulatedSamples) + 1.0));
    imageStore(ImgNormal, imgCoord, vec4(newNormal, 1.0));

    if (gl_GlobalInvocationID.x == 0)
    {
        // Reset data for next frame
        wavefrontPTSSBO.DispatchCommand.NumGroupsX = 0;
        wavefrontPTSSBO.DispatchCommand.NumGroupsY = 1;
        wavefrontPTSSBO.DispatchCommand.NumGroupsZ = 1;
        
        wavefrontPTSSBO.Counts[0] = 0u;
        wavefrontPTSSBO.Counts[1] = 0u;
    }
}

vec3 TurboColormap(float x)
{
    // Source: https://research.google/blog/turbo-an-improved-rainbow-colormap-for-visualization/
    
    const vec4 kRedVec4 = vec4(0.13572138, 4.61539260, -42.66032258, 132.13108234);
    const vec4 kGreenVec4 = vec4(0.09140261, 2.19418839, 4.84296658, -14.18503333);
    const vec4 kBlueVec4 = vec4(0.10667330, 12.64194608, -60.58204836, 110.36276771);
    const vec2 kRedVec2 = vec2(-152.94239396, 59.28637943);
    const vec2 kGreenVec2 = vec2(4.27729857, 2.82956604);
    const vec2 kBlueVec2 = vec2(-89.90310912, 27.34824973);

    x = clamp(x, 0, 1);
    vec4 v4 = vec4( 1.0, x, x * x, x * x * x);
    vec2 v2 = v4.zw * v4.z;
    return vec3(
        dot(v4, kRedVec4)   + dot(v2, kRedVec2),
        dot(v4, kGreenVec4) + dot(v2, kGreenVec2),
        dot(v4, kBlueVec4)  + dot(v2, kBlueVec2)
    );
}