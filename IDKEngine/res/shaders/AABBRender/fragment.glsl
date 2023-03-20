#version 460 core

layout(binding = 0) restrict writeonly uniform image2D ImgResult;

void main()
{
    const vec4 boxColor = vec4(0.0, 1.0, 0.0, 1.0);
    imageStore(ImgResult, ivec2(gl_FragCoord.xy), boxColor);
}