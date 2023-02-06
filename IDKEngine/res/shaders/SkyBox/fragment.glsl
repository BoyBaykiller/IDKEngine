#version 460 core
#extension GL_ARB_bindless_texture : require

layout(location = 0) out vec4 FragColor;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

in InOutVars
{
    vec3 TexCoord;
} inData;

void main()
{
    FragColor = texture(skyBoxUBO.Albedo, inData.TexCoord);
    // unfortunately simply doing gl_Position.xyww is not sufficient (on nvidia)
    gl_FragDepth = 1.0;
    
    // TODO: Implement velocity?
}