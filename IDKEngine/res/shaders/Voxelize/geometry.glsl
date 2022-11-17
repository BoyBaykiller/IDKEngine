#version 460 core

// Inserted by application.
#define HAS_CONSERVATIVE_RASTER __hasConservativeRaster__

layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

layout(binding = 0, rgba16f) restrict uniform image3D ImgVoxels;

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
    flat uint MaterialIndex;
} inData[];

out InOutVars
{
    vec2 TexCoord;
    vec3 FragPos;
    flat uint MaterialIndex;
} outData;

void main()
{
	vec3 normalWeights = abs(inData[0].Normal + inData[1].Normal + inData[2].Normal);

    int dominantAxis = normalWeights.y > normalWeights.x ? 1 : 0;
    dominantAxis = normalWeights.z > normalWeights[dominantAxis] ? 2 : dominantAxis;

    vec3 outClipPositions[3];
    for (int i = 0; i < 3; i++)
    {
        vec3 clipPosition = (vxgiDataUBO.OrthoProjection * gl_in[i].gl_Position).xyz;

        // Select the projection plane that yields the biggest projection area 
        if (dominantAxis == 0) clipPosition = clipPosition.zyx;
        else if (dominantAxis == 1) clipPosition = clipPosition.xzy;

        outClipPositions[i] = clipPosition;
    }

#if !HAS_CONSERVATIVE_RASTER
    /// Conservative Rasterization
    // Source: https://wickedengine.net/2017/08/30/voxel-based-global-illumination/
    vec3 texelSize = 1.0 / imageSize(ImgVoxels);
    vec3 side0N = normalize(outClipPositions[1] - outClipPositions[0]);
    vec3 side1N = normalize(outClipPositions[2] - outClipPositions[1]);
    vec3 side2N = normalize(outClipPositions[0] - outClipPositions[2]);

    outClipPositions[0] += normalize(side2N - side0N) * texelSize;
    outClipPositions[1] += normalize(side0N - side1N) * texelSize;
    outClipPositions[2] += normalize(side1N - side2N) * texelSize;
#endif

    for (int i = 0; i < 3; i++)
    {
        outData.MaterialIndex = inData[i].MaterialIndex;
        outData.TexCoord = inData[i].TexCoord;
        outData.FragPos = gl_in[i].gl_Position.xyz;
    
        gl_Position = vec4(outClipPositions[i], 1.0);

        EmitVertex();
    }
    EndPrimitive();
}
