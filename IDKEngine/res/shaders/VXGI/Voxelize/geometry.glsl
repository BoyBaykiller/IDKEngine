#version 460 core
#extension GL_NV_geometry_shader_passthrough : enable
#if !defined GL_NV_geometry_shader_passthrough
#error "You should only compile this shader if GL_NV_geometry_shader_passthrough is supported. Otherwise use the fallback path"
#endif

layout(triangles) in;

layout(passthrough) in gl_PerVertex
{
    vec4 gl_Position;
} gl_in[];

layout(passthrough) in InOutVars
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} inData[];

void main()
{
    vec3 p1 = gl_in[1].gl_Position.xyz - gl_in[0].gl_Position.xyz;
    vec3 p2 = gl_in[2].gl_Position.xyz - gl_in[0].gl_Position.xyz;
    vec3 normalWeights = abs(cross(p1, p2));

    int dominantAxis = normalWeights.y > normalWeights.x ? 1 : 0;
    dominantAxis = normalWeights.z > normalWeights[dominantAxis] ? 2 : dominantAxis;

    // Swizzle is applied by selecting a viewport
    // This works using the GL_NV_viewport_swizzle extension
    gl_ViewportIndex = 2 - dominantAxis;
}

