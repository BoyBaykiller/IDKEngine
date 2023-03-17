#version 460 core
#extension GL_ARB_bindless_texture : require

// Inserted by application. 1 if NV_geometry_shader_passthrough and NV_viewport_swizzle are supported else 0
#define TAKE_FAST_GEOMETRY_SHADER_PATH __TAKE_FAST_GEOMETRY_SHADER_PATH__

#if TAKE_FAST_GEOMETRY_SHADER_PATH
#extension GL_NV_geometry_shader_passthrough : require
#endif

layout(triangles) in;

#if TAKE_FAST_GEOMETRY_SHADER_PATH
layout(passthrough)
#endif
in InOutVars
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} inData[];

int GetDominantAxis(vec3 p0, vec3 p1, vec3 p2);

#if TAKE_FAST_GEOMETRY_SHADER_PATH
layout(passthrough) in gl_PerVertex
{
    vec4 gl_Position;
} gl_in[];

void main()
{
    int dominantAxis = GetDominantAxis(gl_in[0].gl_Position.xyz, gl_in[1].gl_Position.xyz, gl_in[2].gl_Position.xyz);

    // Swizzle is applied by selecting a viewport
    // This works using the GL_NV_viewport_swizzle extension
    gl_ViewportIndex = 2 - dominantAxis;
}
#else
layout(triangle_strip, max_vertices = 3) out;
layout(binding = 0, rgba16f) restrict uniform image3D ImgResult;

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
    int dominantAxis = GetDominantAxis(gl_in[0].gl_Position.xyz, gl_in[1].gl_Position.xyz, gl_in[2].gl_Position.xyz);

    vec3 outNormDeviceCoords[3];
    for (int i = 0; i < 3; i++)
    {
        vec3 ndc = gl_in[i].gl_Position.xyz;

        // Select the projection plane that yields the biggest projection area 
        if (dominantAxis == 0) ndc = ndc.zyx;
        else if (dominantAxis == 1) ndc = ndc.xzy;

        outNormDeviceCoords[i] = ndc;
    }

    // Dilate Triangle
    // Source: https://wickedengine.net/2017/08/30/voxel-based-global-illumination/
    vec2 viewportPixelSize = 1.0 / imageSize(ImgResult).xy;
    vec2 side0N = normalize(outNormDeviceCoords[1].xy - outNormDeviceCoords[0].xy);
    vec2 side1N = normalize(outNormDeviceCoords[2].xy - outNormDeviceCoords[1].xy);
    vec2 side2N = normalize(outNormDeviceCoords[0].xy - outNormDeviceCoords[2].xy);

    outNormDeviceCoords[0].xy += normalize(side2N - side0N) * viewportPixelSize;
    outNormDeviceCoords[1].xy += normalize(side0N - side1N) * viewportPixelSize;
    outNormDeviceCoords[2].xy += normalize(side1N - side2N) * viewportPixelSize;

    for (int i = 0; i < 3; i++)
    {
        outData.FragPos = inData[i].FragPos;
        outData.TexCoord = inData[i].TexCoord;
        outData.Normal = inData[i].Normal;
        outData.MaterialIndex = inData[i].MaterialIndex;
        outData.EmissiveBias = inData[i].EmissiveBias;
    
        gl_Position = vec4(outNormDeviceCoords[i], 1.0);

        EmitVertex();
    }
    EndPrimitive();
}
#endif

int GetDominantAxis(vec3 v0, vec3 v1, vec3 v2)
{
    vec3 p1 = v1 - v0;
    vec3 p2 = v2 - v0;
    vec3 normalWeights = abs(cross(p1, p2));

    int dominantAxis = normalWeights.y > normalWeights.x ? 1 : 0;
    return normalWeights.z > normalWeights[dominantAxis] ? 2 : dominantAxis;
}
