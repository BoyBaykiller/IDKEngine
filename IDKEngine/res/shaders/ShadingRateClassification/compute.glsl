#version 460 core
#define SHADING_RATE_1_INVOCATION_PER_PIXEL_NV 0u
#define SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV 1u
#define SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV 2u
#define SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV 3u
#define SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV 4u

#define TILE_SIZE 16
layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

layout(binding = 0, r32ui) restrict writeonly uniform uimage2D ImgResult;
layout(binding = 1, rgba16f) restrict uniform image2D ImgShaded;
layout(binding = 0) uniform sampler2D SamplerVelocity;

layout(std140, binding = 5) uniform TaaDataUBO
{
    vec4 Jitters[18 / 2];
    int Samples;
    int Enabled;
    int Frame;
    float VelScale;
} taaDataUBO;

uniform bool IsDebug;
uniform float Aggressiveness;

shared uint DebugShadingRate;
shared vec2 SharedVelocity[TILE_SIZE][TILE_SIZE];

const float AVG_MULTIPLIER = 1.0 / (TILE_SIZE * TILE_SIZE);
void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / textureSize(SamplerVelocity, 0);
    vec2 vel = (texture(SamplerVelocity, uv).rg / taaDataUBO.VelScale) * AVG_MULTIPLIER;
    SharedVelocity[gl_LocalInvocationID.x][gl_LocalInvocationID.y] = abs(vel * Aggressiveness);
    barrier();

    if (gl_LocalInvocationIndex == 0)
    {
        vec2 avgVelocity = vec2(0.0);
        for (int i = 0; i < TILE_SIZE; i++)
        {
            for (int j = 0; j < TILE_SIZE; j++)
            {
                avgVelocity += SharedVelocity[i][j];
            }
        }

        float shadingRateFactor = max(avgVelocity.x, avgVelocity.y);
        uint rate = SHADING_RATE_1_INVOCATION_PER_PIXEL_NV;
        if (shadingRateFactor > 0.1)
		{
			rate = SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV;
		}
		else if (shadingRateFactor > 0.01)
		{
			rate = SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV;
		}
		else if (shadingRateFactor > 0.005)
		{
			rate = SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV;
		}
		else if (shadingRateFactor > 0.001)
		{
			rate = SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV;
		}

        if (IsDebug)
            DebugShadingRate = rate;

        imageStore(ImgResult, ivec2(gl_WorkGroupID.xy), uvec4(rate));
    }

    if (IsDebug)
    {
        barrier();
        
        vec3 debugcolor = vec3(0.0);
        if (DebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X4_PIXELS_NV)
            debugcolor = vec3(4, 0, 0);
        else if (DebugShadingRate == SHADING_RATE_1_INVOCATION_PER_4X2_PIXELS_NV)
            debugcolor = vec3(4, 4, 0);
        else if (DebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X2_PIXELS_NV)
            debugcolor = vec3(0, 4, 0);
        else if (DebugShadingRate == SHADING_RATE_1_INVOCATION_PER_2X1_PIXELS_NV)
            debugcolor = vec3(0, 0, 4);

        vec3 current = imageLoad(ImgShaded, imgCoord).rgb;
        debugcolor += current;

        if (gl_LocalInvocationID.x == 0 || gl_LocalInvocationID.y == 0)
        {
            debugcolor = vec3(0.0);
        }
        
        imageStore(ImgShaded, imgCoord, vec4(debugcolor, 1.0));
    }
}