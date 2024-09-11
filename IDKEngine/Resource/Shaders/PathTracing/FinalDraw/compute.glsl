#version 460 core

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(PathTracing/include/Constants.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict uniform image2D ImgResult;

struct HitInfo
{
    vec3 Bary;
    float T;
    uvec3 VertexIndices;
    uint InstanceID;
};

layout(std140, binding = 7) uniform SettingsUBO
{
    float FocalLength;
    float LenseRadius;
    bool IsDebugBVHTraversal;
    bool IsTraceLights;
    bool TintOnTransmissiveRay;
} settingsUBO;

vec3 TurboColormap(float x);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;
    GpuWavefrontRay wavefrontRay = wavefrontRaySSBO.Rays[rayIndex];

    vec3 irradiance = wavefrontRay.Radiance;
    if (settingsUBO.IsDebugBVHTraversal)
    {
        float x = wavefrontRay.PreviousIOROrDebugNodeCounter / 150.0;
        vec3 col = TurboColormap(x);
        irradiance = col;
    }

    vec3 lastFrameIrradiance = imageLoad(ImgResult, imgCoord).rgb;
    irradiance = mix(lastFrameIrradiance, irradiance, 1.0 / (float(wavefrontPTSSBO.AccumulatedSamples) + 1.0));
    imageStore(ImgResult, imgCoord, vec4(irradiance, 1.0));

    // Reset global memory for next frame
    if (gl_GlobalInvocationID.x == 0)
    {
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