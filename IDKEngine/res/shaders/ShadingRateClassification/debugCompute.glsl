#version 460 core

AppInclude(ShadingRateClassification/include/Constants.glsl)

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerSrc;
layout(binding = 1) uniform usampler2D SamplerDebugShadingRate;
layout(binding = 1) uniform sampler2D SamplerDebugOtherData; // speed, luminance, or coefficient of variation of luminance 

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

uniform int DebugMode;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 imgSize = imageSize(ImgResult);

    vec2 uv = (imgCoord + 0.5) / imgSize;
    vec2 scaledUv = ivec2(uv * imgSize) / vec2(imgSize); 

    vec3 debugColor = vec3(0.0);
    if (DebugMode == DEBUG_MODE_SHADING_RATES)
    {
        uint shadingRate = texture(SamplerDebugShadingRate, scaledUv).r;
        vec3 srcColor = texelFetch(SamplerSrc, imgCoord, 0).rgb;
        if (shadingRate == SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV)
        {
            debugColor += vec3(4, 0, 0);
        }
        else if (shadingRate == SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV)
        {
            debugColor += vec3(4, 4, 0);
        }
        else if (shadingRate == SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV)
        {
            debugColor += vec3(0, 4, 0);
        }
        else if (shadingRate == SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV)
        {
            debugColor += vec3(0, 0, 4);
        }
        else if (shadingRate == SHADING_RATE_1_INVOCATION_PER_PIXEL_NV)
        {
            debugColor = srcColor;
        }

        if (shadingRate != SHADING_RATE_1_INVOCATION_PER_PIXEL_NV)
        {
            debugColor = mix(debugColor, srcColor, vec3(0.5));
        }
    }
    else if (DebugMode == DEBUG_MODE_LUMINANCE)
    {
        float meanLuminance = texture(SamplerDebugOtherData, scaledUv).r;
        debugColor = vec3(meanLuminance);
    }
    else if (DebugMode == DEBUG_MODE_LUMINANCE_VARIANCE)
    {
        float coeffOfVariation = texture(SamplerDebugOtherData, scaledUv).r;
        debugColor = vec3(coeffOfVariation) * 0.2;
    }
    else if (DebugMode == DEBUG_MODE_SPEED)
    {
        float meanSpeed = texture(SamplerDebugOtherData, scaledUv).r;
        debugColor = vec3(meanSpeed);
    }

    ivec2 tileLocalCoodinate = ivec2(uv * textureSize(SamplerDebugShadingRate, 0) * TILE_SIZE) % ivec2(TILE_SIZE);
    if (tileLocalCoodinate.x == 0 || tileLocalCoodinate.y == 0)
    {
        debugColor = vec3(0.0);
    }
    
    imageStore(ImgResult, imgCoord, vec4(debugColor, 1.0));
}
