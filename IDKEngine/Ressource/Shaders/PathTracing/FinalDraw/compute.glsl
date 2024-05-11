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
    bool IsAlwaysTintWithAlbedo;
} settingsUBO;

vec3 SpectralJet(float a);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;
    WavefrontRay wavefrontRay = wavefrontRaySSBO.Rays[rayIndex];

    vec3 irradiance = wavefrontRay.Radiance;
    if (settingsUBO.IsDebugBVHTraversal)
    {
        // use visible light spectrum as heatmap
        float a = min(wavefrontRay.PreviousIOROrDebugNodeCounter / 150.0, 1.0);
        vec3 col = SpectralJet(a);
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

vec3 SpectralJet(float a)
{
    // Source: https://www.shadertoy.com/view/ls2Bz1
    
    vec3 c;
    if (a < 0.25)
    {
        c = vec3(0.0, 4.0 * a, 1.0);
    }
    else if (a < 0.5)
    {
        c = vec3(0.0, 1.0, 1.0 + 4.0 * (0.25 - a));
    }
    else if (a < 0.75)
    {
        c = vec3(4.0 * (a - 0.5), 1.0, 0.0);
    }
    else
    {
        c = vec3(1.0, 1.0 + 4.0 * (0.75 - a), 0.0);
    }
    return clamp(c, vec3(0.0), vec3(1.0));
}