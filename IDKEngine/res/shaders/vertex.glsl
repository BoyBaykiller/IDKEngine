#version 460 core

const vec4 data[4] =
{
    vec4( -1.0, -1.0,  0.0, 0.0),
    vec4(  1.0, -1.0,  1.0, 0.0),
    vec4(  1.0,  1.0,  1.0, 1.0),
    vec4( -1.0,  1.0,  0.0, 1.0)
};

out InOutVars
{
    vec2 TexCoord;
} outData;

void main()
{
    vec4 vertex = data[gl_VertexID];
    outData.TexCoord = vertex.zw;
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
}
