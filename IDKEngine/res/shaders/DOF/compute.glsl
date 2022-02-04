#version 460 core
#define PI 3.14159265
layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerDepth;
layout(binding = 1) uniform sampler2D SamplerSrc;
layout(binding = 2) uniform sampler2D SamplerSrcBlured;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

vec3 NDCToWorldSpace(vec3 ndc);

uniform float FocalLength;
uniform float ApertureRadius;


void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    vec2 uv = imgCoord / vec2(imageSize(ImgResult)); 
    vec3 ndc = vec3(uv, texture(SamplerDepth, uv)) * 2.0 - 1.0;

    vec3 fragPos = NDCToWorldSpace(ndc);
    vec3 camDir = vec3(fragPos - basicDataUBO.ViewPos);
    vec3 focalPoint = basicDataUBO.ViewPos + camDir * FocalLength;
    
    float dist = length(focalPoint - fragPos);

    float blur = clamp((PI * ApertureRadius * ApertureRadius) * dist, 0.0, 1.0);
    vec3 color = mix(texture(SamplerSrc, uv).rgb, texture(SamplerSrcBlured, uv).rgb, blur);

    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}

vec3 NDCToWorldSpace(vec3 ndc)
{
    vec4 worldPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);

    return worldPos.xyz / worldPos.w;
}
