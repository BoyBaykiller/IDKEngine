#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -3.4028235e+38

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerLastForwardPass;
layout(binding = 1) uniform sampler2D SamplerVelocity;
layout(binding = 2) uniform sampler2D SamplerDepth;

layout(std140, binding = 5) uniform TaaDataUBO
{
    vec4 Jitters[18 / 2];
    int Samples;
    int Enabled;
    int Frame;
} taaDataUBO;

void NeighborhoodClamping(ivec2 imgCoord, out ivec2 bestPixel, out vec3 current, out vec3 neighborhoodMin, out vec3 neighborhoodMax);

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    ivec2 bestPixel;
    vec3 neighborhoodMin, neighborhoodMax;
    vec3 current;
    NeighborhoodClamping(imgCoord, bestPixel, current, neighborhoodMin, neighborhoodMax);

    vec2 velocity = texelFetch(SamplerVelocity, bestPixel, 0).rg;
    vec2 oldUV = uv - velocity;
    if (any(greaterThan(oldUV, vec2(1.0))) || any(lessThan(oldUV, vec2(0.0))))
    {
        imageStore(ImgResult, imgCoord, vec4(current, 1.0));
        return;
    }

    vec3 history = texture(SamplerLastForwardPass, oldUV).rgb;
    history = clamp(history, neighborhoodMin, neighborhoodMax);

    // Source: https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/temporalaaCS.hlsl#L121
    ivec2 size = imageSize(ImgResult);
    float subpixelCorrection = fract(max(abs(velocity.x) * size.x, abs(velocity.y) * size.y)) * 0.5;
    
    float blend = 1.0 / taaDataUBO.Samples;
    blend = mix(blend, 0.8, subpixelCorrection);

    vec3 color = mix(history, current, blend);
    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}

// Source: https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/temporalaaCS.hlsl#L74
void NeighborhoodClamping(ivec2 imgCoord, out ivec2 bestPixel, out vec3 current, out vec3 neighborhoodMin, out vec3 neighborhoodMax)
{
    float bestDepth = 1.0;
    neighborhoodMin = vec3(FLOAT_MAX);
    neighborhoodMax = vec3(FLOAT_MIN);
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            ivec2 curPixel = imgCoord + ivec2(x, y);
    
            vec3 neighbor = imageLoad(ImgResult, curPixel).rgb;
            neighborhoodMin = min(neighborhoodMin, neighbor);
            neighborhoodMax = max(neighborhoodMax, neighbor);
            
            if (x == 0 && y == 0)
            {
                current = neighbor;
            }
    
            float currentDepth = texelFetch(SamplerDepth, curPixel, 0).r;
            if (currentDepth < bestDepth)
            {
                bestDepth = currentDepth;
                bestPixel = curPixel;
            }
        }
    }
}