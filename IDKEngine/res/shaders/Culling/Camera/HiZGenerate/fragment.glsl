#version 460 core
#extension GL_AMD_texture_gather_bias_lod : enable

layout(binding = 0) uniform sampler2D SamplerDepth;

layout(location = 0) uniform int Lod;
layout(location = 1) uniform bool WasEven;

in InOutVars
{
    vec2 TexCoord;
} inData;

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);
    vec2 sampleHiZSize = textureSize(SamplerDepth, Lod);

    float furthestDepth = 0.0;
    if (WasEven)
    {
        vec2 writeHiZSize = textureSize(SamplerDepth, Lod + 1);
        vec2 scaled_pos = imgCoord * (sampleHiZSize / writeHiZSize);

        vec4 depths;// = textureGatherLodAMD(SamplerDepth, uv, Lod);
        depths.x = texelFetch(SamplerDepth, imgCoord * 2 + ivec2(0, 0), Lod).r;
        depths.y = texelFetch(SamplerDepth, imgCoord * 2 + ivec2(1, 0), Lod).r;
        depths.z = texelFetch(SamplerDepth, imgCoord * 2 + ivec2(0, 1), Lod).r;
        depths.w = texelFetch(SamplerDepth, imgCoord * 2 + ivec2(1, 1), Lod).r;

        furthestDepth = max(max(depths.x, depths.y), max(depths.z, depths.w));
    }
    else
    {
        ivec2 offsets[] = {
            ivec2(-1, -1),
            ivec2( 0, -1),
            ivec2( 1, -1),
            ivec2(-1,  0),
            ivec2( 0,  0),
            ivec2( 1,  0),
            ivec2(-1,  1),
            ivec2( 0,  1),
            ivec2( 1,  1)
        };

        vec2 texelSize = 1.0 / sampleHiZSize;
        for (int i = 0; i < offsets.length(); i++)
        {
            vec2 pos = inData.TexCoord + offsets[i] * texelSize;
            furthestDepth = max(furthestDepth, texelFetch(SamplerDepth, ivec2(pos * sampleHiZSize), Lod).r);
        }
    }
    

    gl_FragDepth = furthestDepth;
}