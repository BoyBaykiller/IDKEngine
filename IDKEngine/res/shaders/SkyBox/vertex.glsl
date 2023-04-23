#version 460 core

const vec3 positions[24] =
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

AppInclude(shaders/include/Buffers.glsl)

out InOutVars
{
    vec3 TexCoord;
    vec4 ClipPos;
    vec4 PrevClipPos;
} outData;

void main()
{
    mat4 viewNoTranslation = mat4(basicDataUBO.View[0], basicDataUBO.View[1], basicDataUBO.View[2], vec4(0.0, 0.0, 0.0, 1.0));
    mat4 prevViewNoTranslation = mat4(basicDataUBO.PrevView[0], basicDataUBO.PrevView[1], basicDataUBO.PrevView[2], vec4(0.0, 0.0, 0.0, 1.0));
    outData.TexCoord = positions[gl_VertexID];

    outData.ClipPos = (basicDataUBO.Projection * viewNoTranslation * vec4(outData.TexCoord, 1.0));
    outData.PrevClipPos = (basicDataUBO.Projection * prevViewNoTranslation * vec4(outData.TexCoord, 1.0));
    
    gl_Position = outData.ClipPos;
    gl_Position.w = gl_Position.z;
}