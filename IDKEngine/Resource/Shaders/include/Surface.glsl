#define SURFACE_EMISSIVE_FACTOR 1.0

AppInclude(include/GpuTypes.glsl);
AppInclude(include/Compression.glsl)

struct Surface
{
    vec3 Albedo;
    float Alpha;

    vec3 Normal;
    vec3 Emissive;
    vec3 Absorbance;

    float Metallic;
    float Roughness; 
    float Transmission;
    float IOR;
    
    float AlphaCutoff;
    bool IsVolumetric; // Opposite of ThinWalled, affects transmission behaviour
    bool TintOnTransmissive;
};

Surface GetDefaultSurface()
{
    Surface surface;

    surface.Albedo = vec3(1.0);
    surface.Alpha = 1.0;

    surface.Normal = vec3(0.0);
    surface.Emissive = vec3(0.0);
    surface.Absorbance = vec3(0.0);

    surface.Metallic = 0.0;
    surface.Roughness = 0.0;
    surface.Transmission = 0.0;
    surface.IOR = 1.5;

    surface.AlphaCutoff = 0.5;

    surface.IsVolumetric = false;
    surface.TintOnTransmissive = true;
    
    return surface;
}

Surface GetSurface(GpuMaterial gpuMaterial, vec2 uv, float baseColorLodBias)
{
    Surface surface;

#if APP_SHADER_STAGE_FRAGMENT
    vec4 baseColorAndAlpha = texture(gpuMaterial.BaseColor, uv, baseColorLodBias) * DecompressUR8G8B8A8(gpuMaterial.BaseColorFactor);
#else
    // Normally the GL does not try to compute automatic derivatives in shaders other than fragment.
    // However when using the lod bias overload it does (on AMD) which gives incorrect results. Therefore we ignore lod bias here.
    vec4 baseColorAndAlpha = texture(gpuMaterial.BaseColor, uv) * DecompressUR8G8B8A8(gpuMaterial.BaseColorFactor);
#endif

    surface.Albedo = baseColorAndAlpha.rgb;
    surface.Alpha = baseColorAndAlpha.a;

    surface.Normal = ReconstructPackedNormal(texture(gpuMaterial.Normal, uv).rg);
    surface.Emissive = texture(gpuMaterial.Emissive, uv).rgb * gpuMaterial.EmissiveFactor;
    surface.Absorbance = gpuMaterial.Absorbance;

    surface.Metallic = texture(gpuMaterial.MetallicRoughness, uv).r * gpuMaterial.MetallicFactor;
    surface.Roughness = texture(gpuMaterial.MetallicRoughness, uv).g * gpuMaterial.RoughnessFactor;
    surface.Transmission = texture(gpuMaterial.Transmission, uv).r * gpuMaterial.TransmissionFactor;
    surface.IOR = gpuMaterial.IOR;

    surface.AlphaCutoff = gpuMaterial.AlphaCutoff;
    surface.IsVolumetric = gpuMaterial.IsVolumetric;

    return surface;
}

Surface GetSurface(GpuMaterial gpuMaterial, vec2 uv)
{
    Surface surface = GetSurface(gpuMaterial, uv, 0.0);
    return surface;
}

void SurfaceApplyModificatons(inout Surface surface, GpuMesh mesh)
{
    surface.Emissive = surface.Emissive * SURFACE_EMISSIVE_FACTOR + mesh.EmissiveBias * surface.Albedo;
    surface.Absorbance = max(surface.Absorbance + mesh.AbsorbanceBias, vec3(0.0));

    surface.Metallic = clamp(surface.Metallic + mesh.SpecularBias, 0.0, 1.0);
    surface.Roughness = clamp(surface.Roughness + mesh.RoughnessBias, 0.0, 1.0);
    surface.Transmission = clamp(surface.Transmission + mesh.TransmissionBias, 0.0, 1.0);
    surface.IOR = max(surface.IOR + mesh.IORBias, 1.0);

    surface.TintOnTransmissive = mesh.TintOnTransmissive;
}

bool SurfaceHasAlphaBlending(Surface surface)
{
    // Keep in sync between shader and client code!
    const float valueMeaniningBlendMode = 2.0;
    
    return surface.AlphaCutoff == valueMeaniningBlendMode;
}