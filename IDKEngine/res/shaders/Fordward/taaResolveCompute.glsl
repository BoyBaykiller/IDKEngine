#version 460 core

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerOldResult;
layout(binding = 1) uniform sampler2D SamplerVelocity;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / vec2(imageSize(ImgResult));
    vec2 oldUV = uv - texture(SamplerVelocity, uv).rg;

    vec3 oldColor = texture(SamplerOldResult, uv).rgb;
    vec3 thisColor = imageLoad(ImgResult, imgCoord).rgb;
    thisColor = mix(oldColor, thisColor, 0.5);

    imageStore(ImgResult, imgCoord, vec4(thisColor, 1.0));
}