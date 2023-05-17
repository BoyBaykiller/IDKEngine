#version 460 core

AppInclude(ShadingRateClassification/include/Constants.glsl)

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform usampler2D SamplerDebugShadingRate;
layout(binding = 0) uniform sampler2D SamplerDebugOtherData; // speed, luminance, or luminance variance 

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    float _pad0;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

uniform int DebugMode;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / textureSize(SamplerDebugShadingRate, 0);

    vec3 srcColor = imageLoad(ImgResult, imgCoord).rgb;

    vec3 debugColor = srcColor;
    if (DebugMode == DEBUG_MODE_SHADING_RATES)
    {
        uint shadingRate = texelFetch(SamplerDebugShadingRate, ivec2(gl_WorkGroupID.xy), 0).r;
        if (shadingRate == SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV)
            debugColor += vec3(4, 0, 0);
        else if (shadingRate == SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV)
            debugColor += vec3(4, 4, 0);
        else if (shadingRate == SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV)
            debugColor += vec3(0, 4, 0);
        else if (shadingRate == SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV)
            debugColor += vec3(0, 0, 4);
    }
    else if (DebugMode == DEBUG_MODE_LUMINANCE)
    {
        float meanLuminance = texelFetch(SamplerDebugOtherData, ivec2(gl_WorkGroupID.xy), 0).r;
        debugColor = vec3(meanLuminance);
    }
    else if (DebugMode == DEBUG_MODE_LUMINANCE_VARIANCE)
    {
        float normalizedVariance = texelFetch(SamplerDebugOtherData, ivec2(gl_WorkGroupID.xy), 0).r;
        debugColor = vec3(normalizedVariance) * 0.2;
    }
    else if (DebugMode == DEBUG_MODE_SPEED)
    {
        float meanSpeed = texelFetch(SamplerDebugOtherData, ivec2(gl_WorkGroupID.xy), 0).r;
        debugColor = vec3(meanSpeed);
    }

    if (gl_LocalInvocationID.x == 0 || gl_LocalInvocationID.y == 0)
    {
        debugColor = vec3(0.0);
    }
    
    imageStore(ImgResult, imgCoord, vec4(debugColor, 1.0));
}
