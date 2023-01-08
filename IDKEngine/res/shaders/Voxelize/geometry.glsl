#version 460 core
#extension GL_ARB_bindless_texture : require // somehow required to be here on nvidia

layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

#ifdef GL_NV_shader_atomic_fp16_vector
layout(binding = 0, rgba16f) restrict uniform image3D ImgVoxelsAlbedo;
#else
layout(binding = 0, r32ui) restrict uniform uimage3D ImgVoxelsAlbedo;
#endif

layout(std140, binding = 5) uniform VXGIDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} vxgiDataUBO;

in InOutVars
{
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} inData[];

out InOutVars
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} outData;

void main()
{
    vec3 p1 = gl_in[1].gl_Position.xyz - gl_in[0].gl_Position.xyz;
    vec3 p2 = gl_in[2].gl_Position.xyz - gl_in[0].gl_Position.xyz;
    vec3 normalWeights = abs(cross(p1, p2));
    // vec3 normalWeights = abs(inData[0].Normal + inData[1].Normal + inData[2].Normal);

    int dominantAxis = normalWeights.y > normalWeights.x ? 1 : 0;
    dominantAxis = normalWeights.z > normalWeights[dominantAxis] ? 2 : dominantAxis;

    vec3 outNormDeviceCoords[3];
    for (int i = 0; i < 3; i++)
    {
        vec3 ndc = (vxgiDataUBO.OrthoProjection * gl_in[i].gl_Position).xyz;

        // Select the projection plane that yields the biggest projection area 
        if (dominantAxis == 0) ndc = ndc.zyx;
        else if (dominantAxis == 1) ndc = ndc.xzy;

        outNormDeviceCoords[i] = ndc;
    }

    // Dilate Triangle
    // Source: https://wickedengine.net/2017/08/30/voxel-based-global-illumination/
    vec2 viewportPixelSize = 1.0 / imageSize(ImgVoxelsAlbedo).xy;
    vec2 side0N = normalize(outNormDeviceCoords[1].xy - outNormDeviceCoords[0].xy);
    vec2 side1N = normalize(outNormDeviceCoords[2].xy - outNormDeviceCoords[1].xy);
    vec2 side2N = normalize(outNormDeviceCoords[0].xy - outNormDeviceCoords[2].xy);

    outNormDeviceCoords[0].xy += normalize(side2N - side0N) * viewportPixelSize;
    outNormDeviceCoords[1].xy += normalize(side0N - side1N) * viewportPixelSize;
    outNormDeviceCoords[2].xy += normalize(side1N - side2N) * viewportPixelSize;

    for (int i = 0; i < 3; i++)
    {
        outData.FragPos = gl_in[i].gl_Position.xyz;
        outData.TexCoord = inData[i].TexCoord;
        outData.Normal = inData[i].Normal;
        outData.MaterialIndex = inData[i].MaterialIndex;
        outData.EmissiveBias = inData[i].EmissiveBias;
    
        gl_Position = vec4(outNormDeviceCoords[i], 1.0);

        EmitVertex();
    }
    EndPrimitive();
}
