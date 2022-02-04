#version 460 core
#define EPSILON 0.001

layout(local_size_x = 8, local_size_y = 4, local_size_z = 1) in;

layout(binding = 0, rgba16f) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler2D SamplerSrc;
layout(binding = 1) uniform sampler2D SamplerNormalSpec;
layout(binding = 2) uniform sampler2D SamplerDepth;
layout(binding = 3) uniform samplerCube SamplerEnvironment;


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

vec3 SSR(vec3 normal, vec3 fragPos);
vec3 WorldToNDC(vec3 worldPos);
vec3 NDCToWorldSpace(vec3 ndc);

uniform int Samples = 20;
uniform int MaxDist = 50;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);

    vec2 SamplerNormalSpecUV = vec2(imgCoord) / (textureSize(SamplerNormalSpec, 0));
    vec4 normalSpec = texture(SamplerNormalSpec, SamplerNormalSpecUV);
    if (normalSpec.a < EPSILON)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }
    
    vec2 samplerDepthUV = vec2(imgCoord) / (textureSize(SamplerDepth, 0));
    float depth = texture(SamplerDepth, samplerDepthUV).r;

    vec3 fragPos = NDCToWorldSpace(vec3(samplerDepthUV, depth) * 2.0 - 1.0);
    vec3 color = SSR(normalSpec.rgb, fragPos);

    imageStore(ImgResult, imgCoord, vec4(color * normalSpec.a, 1.0));
}

vec3 SSR(vec3 normal, vec3 fragPos)
{
    vec3 reflectDir = reflect(normalize(fragPos - basicDataUBO.ViewPos), normal);
    vec3 maxReflectPoint = fragPos + reflectDir * MaxDist;
    vec3 deltaStep = (maxReflectPoint - fragPos) / Samples; 
    
    vec3 samplePoint = fragPos;
    for (int i = 0; i < Samples; i++)
    {
        samplePoint += deltaStep;
        
        vec3 projectedSample = WorldToNDC(samplePoint) * 0.5 + 0.5;
        
        if (any(greaterThanEqual(projectedSample.xy, vec2(1.0))) || any(lessThan(projectedSample.xy, vec2(0.0))))
        {
            // TODO: Parallax corrected cubemap reflections as fallback? 
            return vec3(0.0);
        }

        float depth = texture(SamplerDepth, projectedSample.xy).r;
        if (projectedSample.z > depth)
            return texture(SamplerSrc, projectedSample.xy).rgb; 

    }

    return texture(SamplerEnvironment, reflectDir).rgb;
}

vec3 WorldToNDC(vec3 worldPos)
{
    vec4 clipPos = basicDataUBO.ProjView * vec4(worldPos, 1.0);
    return clipPos.xyz / clipPos.w;
}

vec3 NDCToWorldSpace(vec3 ndc)
{
    vec4 worldPos = basicDataUBO.InvProjView * vec4(ndc, 1.0);

    return worldPos.xyz / worldPos.w;
}