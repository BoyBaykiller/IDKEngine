#version 460 core

AppInclude(include/Math.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(DeferredLighting/include/Impl.glsl)

layout(location = 0) out vec4 OutFragColor;

layout(binding = 0) uniform sampler2D SamplerAO;
layout(binding = 1) uniform sampler2D SamplerIndirectLighting;

uniform bool IsVXGI;
uniform ENUM_SHADOW_MODE ShadowMode;

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

    float ambientOcclusion = texelFetch(SamplerAO, imgCoord, 0).r;

    Surface surface = GetDefaultSurface();
    surface.Albedo = texelFetch(gBufferDataUBO.AlbedoAlpha, imgCoord, 0).rgb;
    surface.Normal = DecodeUnitVec(texelFetch(gBufferDataUBO.Normal, imgCoord, 0).rg);
    surface.Metallic = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).r;
    surface.Roughness = texelFetch(gBufferDataUBO.MetallicRoughness, imgCoord, 0).g;
    surface.Emissive = texelFetch(gBufferDataUBO.Emissive, imgCoord, 0).rgb;
    surface.IOR = 1.0;

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        GpuLight light = lightsUBO.Lights[i];
        vec3 contribution = EvaluateLighting(light, surface, fragPos, perFrameDataUBO.ViewPos, ambientOcclusion);
        
        if (contribution != vec3(0.0))
        {
            float shadow = 0.0;
            if (light.PointShadowIndex == -1)
            {
                shadow = 0.0;
            }
            else if (ShadowMode == ENUM_SHADOW_MODE_PCF)
            {
                GpuPointShadow pointShadow = shadowsUBO.PointShadows[light.PointShadowIndex];
                vec3 lightToSample = unjitteredFragPos - light.Position;
                shadow = 1.0 - Visibility(pointShadow, surface.Normal, lightToSample);
            }
            else if (ShadowMode == ENUM_SHADOW_MODE_RAY_TRACED)
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
        indirectLight = texelFetch(SamplerIndirectLighting, imgCoord, 0).rgb * surface.Albedo;
    }
    else
    {
        const vec3 ambient = vec3(0.015);
        indirectLight = ambient * surface.Albedo;
    }

    OutFragColor = vec4((directLighting + indirectLight) + surface.Emissive, 1.0);
    // OutFragColor = vec4(alpha);
}
