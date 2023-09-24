#version 460 core

const vec3 positions[] =
{
    // Back
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5, -0.5, -0.5 ),
    vec3( 0.5, -0.5, -0.5 ),
    vec3( 0.5,  0.5, -0.5 ),

    // Front
    vec3(-0.5,  0.5,  0.5 ),
    vec3(-0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5,  0.5 ),
    vec3( 0.5,  0.5,  0.5 ),

    // Left
    vec3(-0.5,  0.5,  0.5 ),
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5, -0.5, -0.5 ),
    vec3(-0.5, -0.5,  0.5 ),

    // Right
    vec3( 0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5, -0.5 ),
    vec3( 0.5, -0.5, -0.5 ),
    vec3( 0.5, -0.5,  0.5 ),

    // Up
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5, -0.5 ),

    // Down
    vec3(-0.5, -0.5, -0.5 ),
    vec3(-0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5, -0.5 )
};

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
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(location = 0) uniform vec3 Min;
layout(location = 1) uniform vec3 Max;

void main()
{
    vec3 boxPos = (Min + Max) * 0.5;
    vec3 boxSize = Max - Min;

    gl_Position = basicDataUBO.ProjView * vec4(boxPos + boxSize * positions[gl_VertexID], 1.0);
}