#version 460 core

AppInclude(include/StaticStorageBuffers.glsl)

AppInclude(include/Pbr.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(location = 0) out vec4 OutFragColor;

layout(binding = 0) uniform sampler2D SamplerAO;
layout(binding = 1) uniform sampler2D SamplerIndirectLighting;

vec3 EvaluateLighting(GpuLight light, Surface surface, vec3 fragPos, vec3 viewPos, float ambientOcclusion);
float Visibility(GpuPointShadow pointShadow, vec3 normal, vec3 lightToSample);
float GetLightSpaceDepth(GpuPointShadow pointShadow, vec3 lightSpaceSamplePos);

#define SHADOW_MODE_NONE 0
#define SHADOW_MODE_PCF_SHADOW_MAP 1
#define SHADOW_MODE_RAY_TRACED 2
uniform int ShadowMode;

uniform bool IsVXGI;

in InOutData
{
    vec2 TexCoord;
} inData;

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);
    vec2 uv = inData.TexCoord;
    
    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 1.0)
    {
        OutFragColor = vec4(0.0);
        return;
    }
    
    // InitializeRandomSeed((imgCoord.y * 4096 + imgCoord.x) * (perFrameDataUBO.Frame + 1));

    vec3 ndc = vec3(uv * 2.0 - 1.0, depth);
    vec3 fragPos = PerspectiveTransform(ndc, perFrameDataUBO.InvProjView);
    vec3 unjitteredFragPos = PerspectiveTransform(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), perFrameDataUBO.InvProjView);

    vec3 albedo = texelFetch(gBufferDataUBO.AlbedoAlpha, imgCoord, 0).rgb;
    float alpha = texelFetch(gBufferDataUBO.AlbedoAlpha, imgCoord, 0).a;
    vec3 normal = DecodeUnitVec(texelFetch(gBufferDataUBO.Normal, imgCoord, 0).rg);
    float specular = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).r;
    float roughness = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).g;
    vec3 emissive = texelFetch(gBufferDataUBO.Emissive, imgCoord, 0).rgb;
    float ambientOcclusion = 1.0 - texelFetch(SamplerAO, imgCoord, 0).r;

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        GpuLight light = lightsUBO.Lights[i];

        Surface surface = GetDefaultSurface();
        surface.Albedo = albedo;
        surface.Normal = normal;
        surface.Metallic = specular;
        surface.Roughness = roughness;

        // TODO: Use real value
        surface.IOR = 1.0;

        vec3 contribution = EvaluateLighting(light, surface, fragPos, perFrameDataUBO.ViewPos, ambientOcclusion);
        
        if (contribution != vec3(0.0))
        {
            float shadow = 0.0;
            if (light.PointShadowIndex == -1)
            {
                shadow = 0.0;
            }
            else if (ShadowMode == SHADOW_MODE_PCF_SHADOW_MAP)
            {
                GpuPointShadow pointShadow = shadowsUBO.PointShadows[light.PointShadowIndex];
                vec3 lightToSample = unjitteredFragPos - light.Position;
                shadow = 1.0 - Visibility(pointShadow, normal, lightToSample);
            }
            else if (ShadowMode == SHADOW_MODE_RAY_TRACED)
            {
                GpuPointShadow pointShadow = shadowsUBO.PointShadows[light.PointShadowIndex];
                shadow = imageLoad(image2D(pointShadow.RayTracedShadowMapImage), imgCoord).r;
            }

            contribution *= (1.0 - shadow);
        }

        directLighting += contribution;
    }

    vec3 indirectLight;
    if (IsVXGI)
    {
        indirectLight = texelFetch(SamplerIndirectLighting, imgCoord, 0).rgb * albedo;
    }
    else
    {
        const vec3 ambient = vec3(0.015);
        indirectLight = ambient * albedo;
    }

    OutFragColor = vec4((directLighting + indirectLight) + emissive, 1.0);
    // OutFragColor = vec4(normal * 0.5 + 0.5, 1.0);
}

vec3 EvaluateLighting(GpuLight light, Surface surface, vec3 fragPos, vec3 viewPos, float ambientOcclusion)
{
    vec3 surfaceToLight = light.Position - fragPos;
    vec3 dirSurfaceToCam = normalize(viewPos - fragPos);
    vec3 dirSurfaceToLight = normalize(surfaceToLight);
    
    float distSq = dot(surfaceToLight, surfaceToLight);
    float attenuation = GetAttenuationFactor(distSq, light.Radius);

    
    const float prevIor = 1.0;
    vec3 fresnelTerm;
    vec3 specularBrdf = GGXBrdf(surface, dirSurfaceToCam, dirSurfaceToLight, prevIor, fresnelTerm);
    vec3 diffuseBrdf = surface.Albedo * ambientOcclusion;
    
    vec3 combinedBrdf = specularBrdf + diffuseBrdf * (vec3(1.0) - fresnelTerm) * (1.0 - surface.Metallic); 

    float cosTheta = clamp(dot(surface.Normal, dirSurfaceToLight), 0.0, 1.0);
    return combinedBrdf * attenuation * cosTheta * light.Color;
}

float Visibility(GpuPointShadow pointShadow, vec3 normal, vec3 lightToSample)
{
    // TODO: Use overall better sampling method
    // Source: https://learnopengl.com/Advanced-Lighting/Shadows/Point-Shadows
    const vec3 ShadowSampleOffsets[] =
    {
        vec3( 0.0,  0.0,  0.0 ),
        vec3( 1.0,  1.0,  1.0 ), vec3(  1.0, -1.0,  1.0 ), vec3( -1.0, -1.0,  1.0 ), vec3( -1.0,  1.0,  1.0 ), 
        vec3( 1.0,  1.0, -1.0 ), vec3(  1.0, -1.0, -1.0 ), vec3( -1.0, -1.0, -1.0 ), vec3( -1.0,  1.0, -1.0 ),
        vec3( 1.0,  1.0,  0.0 ), vec3(  1.0, -1.0,  0.0 ), vec3( -1.0, -1.0,  0.0 ), vec3( -1.0,  1.0,  0.0 ),
        vec3( 1.0,  0.0,  1.0 ), vec3( -1.0,  0.0,  1.0 ), vec3(  1.0,  0.0, -1.0 ), vec3( -1.0,  0.0, -1.0 ),
        vec3( 0.0,  1.0,  1.0 ), vec3(  0.0, -1.0,  1.0 ), vec3(  0.0, -1.0, -1.0 ), vec3(  0.0,  1.0, -1.0 )
    };
    
    const float bias = 0.018;
    const float sampleDiskRadius = 0.04;

    float visibilityFactor = 0.0;
    for (int i = 0; i < ShadowSampleOffsets.length(); i++)
    {
        vec3 samplePos = (lightToSample + ShadowSampleOffsets[i] * sampleDiskRadius);
        float depth = GetLightSpaceDepth(pointShadow, samplePos * (1.0 - bias));
        visibilityFactor += texture(pointShadow.PcfShadowTexture, vec4(samplePos, depth));
    }
    visibilityFactor /= ShadowSampleOffsets.length();

    return visibilityFactor;
}

float GetLightSpaceDepth(GpuPointShadow pointShadow, vec3 lightSpaceSamplePos)
{
    float dist = max(abs(lightSpaceSamplePos.x), max(abs(lightSpaceSamplePos.y), abs(lightSpaceSamplePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}