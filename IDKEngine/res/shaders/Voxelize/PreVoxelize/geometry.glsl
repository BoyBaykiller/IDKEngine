#version 460 core

layout(triangles) in;
layout(triangle_strip, max_vertices = 3) out;

layout(std140, binding = 5) uniform VXGIDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} vxgiDataUBO;

out InOutVars
{
    vec3 FragPos;
} outData;

layout(location = 0) uniform vec2 ViewportTexelSize;

void main()
{
    vec3 p1 = gl_in[1].gl_Position.xyz - gl_in[0].gl_Position.xyz;
    vec3 p2 = gl_in[2].gl_Position.xyz - gl_in[0].gl_Position.xyz;
    vec3 normalWeights = abs(cross(p1, p2));

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

    // Expand Triangle
    // Source: https://wickedengine.net/2017/08/30/voxel-based-global-illumination/
    vec2 side0N = normalize(outNormDeviceCoords[1].xy - outNormDeviceCoords[0].xy);
    vec2 side1N = normalize(outNormDeviceCoords[2].xy - outNormDeviceCoords[1].xy);
    vec2 side2N = normalize(outNormDeviceCoords[0].xy - outNormDeviceCoords[2].xy);

    outNormDeviceCoords[0].xy += normalize(side2N - side0N) * ViewportTexelSize;
    outNormDeviceCoords[1].xy += normalize(side0N - side1N) * ViewportTexelSize;
    outNormDeviceCoords[2].xy += normalize(side1N - side2N) * ViewportTexelSize;

    for (int i = 0; i < 3; i++)
    {
        outData.FragPos = gl_in[i].gl_Position.xyz;
        gl_Position = vec4(outNormDeviceCoords[i], 1.0);

        EmitVertex();
    }
    EndPrimitive();
}
