#version 460 core

AppInclude(include/CubeVertices.glsl)
AppInclude(include/GpuTypes.glsl)

layout(location = 0) uniform vec3 Min;
layout(location = 1) uniform vec3 Max;
layout(location = 2) uniform mat4 Matrix;

void main()
{
    vec3 boxPos = (Min + Max) * 0.5;
    vec3 boxSize = Max - Min;

    gl_Position = Matrix * vec4(boxPos + boxSize * CubeVertices[gl_VertexID], 1.0);
}