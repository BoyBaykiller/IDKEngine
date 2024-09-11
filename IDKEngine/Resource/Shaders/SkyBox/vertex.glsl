#version 460 core

AppInclude(include/CubeVertices.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

out InOutData
{
    vec3 TexCoord;
    vec4 ClipPos;
    vec4 PrevClipPos;
} outData;

void main()
{
    mat4 viewNoTranslation = perFrameDataUBO.View;
    viewNoTranslation[3] = vec4(0.0, 0.0, 0.0, 1.0);

    mat4 prevViewNoTranslation = perFrameDataUBO.PrevView;
    prevViewNoTranslation[3] = vec4(0.0, 0.0, 0.0, 1.0);

    outData.TexCoord = CubeVertices[gl_VertexID];

    outData.ClipPos = (perFrameDataUBO.Projection * viewNoTranslation * vec4(outData.TexCoord, 1.0));
    outData.PrevClipPos = (perFrameDataUBO.Projection * prevViewNoTranslation * vec4(outData.TexCoord, 1.0));
    
    gl_Position = outData.ClipPos;
    gl_Position = gl_Position.xyww;
}