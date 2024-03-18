#version 460 core
layout(location = 0) out vec4 FragColor;

layout(binding = 0) uniform sampler2D Sampler0;

in InOutVars
{
    vec2 TexCoord;
} inData;

void main()
{
    FragColor = texture(Sampler0, inData.TexCoord);
}
