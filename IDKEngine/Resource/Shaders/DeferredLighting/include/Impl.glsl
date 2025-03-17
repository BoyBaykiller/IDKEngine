AppInclude(include/Pbr.glsl)
AppInclude(include/Surface.glsl)
AppInclude(include/GpuTypes.glsl)

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
    vec3 diffuseBrdf = surface.Albedo * (1.0 - ambientOcclusion);
    
    vec3 combinedBrdf = specularBrdf + diffuseBrdf * (vec3(1.0) - fresnelTerm) * (1.0 - surface.Metallic); 

    float cosTheta = clamp(dot(surface.Normal, dirSurfaceToLight), 0.0, 1.0);
    return combinedBrdf * attenuation * cosTheta * light.Color;
}

vec3 EvaluateLighting(GpuLight light, Surface surface, vec3 fragPos, vec3 viewPos)
{
    return EvaluateLighting(light, surface, fragPos, viewPos, 0.0);
}

float GetLightSpaceDepth(GpuPointShadow pointShadow, vec3 lightSpaceSamplePos)
{
    float dist = max(abs(lightSpaceSamplePos.x), max(abs(lightSpaceSamplePos.y), abs(lightSpaceSamplePos.z)));
    float depth = GetLogarithmicDepth(pointShadow.NearPlane, pointShadow.FarPlane, dist);

    return depth;
}

float Visibility(GpuPointShadow pointShadow, vec3 normal, vec3 lightToSample)
{
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
