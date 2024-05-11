#version 460 core

AppInclude(include/CubeVertices.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

out InOutVars
{
    vec3 TexCoord;
    vec4 ClipPos;
    vec4 PrevClipPos;
} outData;

void main()
{
    mat4 viewNoTranslation = mat4(perFrameDataUBO.View[0], perFrameDataUBO.View[1], perFrameDataUBO.View[2], vec4(0.0, 0.0, 0.0, 1.0));
    mat4 prevViewNoTranslation = mat4(perFrameDataUBO.PrevView[0], perFrameDataUBO.PrevView[1], perFrameDataUBO.PrevView[2], vec4(0.0, 0.0, 0.0, 1.0));
    outData.TexCoord = CubeVertices[gl_VertexID];

    outData.ClipPos = (perFrameDataUBO.Projection * viewNoTranslation * vec4(outData.TexCoord, 1.0));
    outData.PrevClipPos = (perFrameDataUBO.Projection * prevViewNoTranslation * vec4(outData.TexCoord, 1.0));
    
    gl_Position = outData.ClipPos;
    gl_Position = gl_Position.xyww;
}