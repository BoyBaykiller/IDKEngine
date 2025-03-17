#version 460 core
layout(location = 0) out vec4 OutFragColor;

AppInclude(include/Math.glsl)
AppInclude(include/Sampling.glsl)
AppInclude(include/Surface.glsl)
AppInclude(include/TraceCone.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(VXGI/ConeTraceGI/include/Impl.glsl)
AppInclude(DeferredLighting/include/Impl.glsl)

layout(binding = 0) uniform sampler3D SamplerVoxels;

#define SHADOW_MODE_NONE 0
#define SHADOW_MODE_PCF_SHADOW_MAP 1
#define SHADOW_MODE_RAY_TRACED 2
uniform int ShadowMode;

uniform bool IsVXGI;

in InOutData
{
    vec2 TexCoord;
    vec4 PrevClipPos;
    vec3 Normal;
    vec3 Tangent;
    flat uint MeshId;
} inData;

layout(std140, binding = 0) uniform SettingsUBO
{
    ConeTraceGISettings ConeTraceSettings;
} settingsUBO;

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);

    GpuMesh mesh = meshSSBO.Meshes[inData.MeshId];
    GpuMaterial material = materialSSBO.Materials[mesh.MaterialId];
    
    Surface surface = GetSurface(material, inData.TexCoord, taaDataUBO.MipmapBias);
    SurfaceApplyModificatons(surface, mesh);

    if (surface.Alpha == 0.0)
    {
        discard;
    }

    vec3 interpTangent = normalize(inData.Tangent);
    vec3 interpNormal = normalize(inData.Normal);
    mat3 tbn = GetTBN(interpTangent, interpNormal);
    surface.Normal = tbn * surface.Normal;
    surface.Normal = normalize(mix(interpNormal, surface.Normal, mesh.NormalMapStrength));

    vec2 uv = gl_FragCoord.xy / textureSize(gBufferDataUBO.Velocity, 0);
    vec3 ndc = vec3(uv * 2.0 - 1.0, gl_FragCoord.z);
    vec3 fragPos = PerspectiveTransform(vec3(ndc.xy, ndc.z), perFrameDataUBO.InvProjView);
    vec3 unjitteredFragPos = PerspectiveTransform(vec3(ndc.xy - taaDataUBO.Jitter, ndc.z), perFrameDataUBO.InvProjView);

    vec3 directLighting = vec3(0.0);
    for (int i = 0; i < lightsUBO.Count; i++)
    {
        GpuLight light = lightsUBO.Lights[i];
        vec3 contribution = EvaluateLighting(light, surface, fragPos, perFrameDataUBO.ViewPos);
        
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
                shadow = 1.0 - Visibility(pointShadow, surface.Normal, lightToSample);
            }
            else if (ShadowMode == SHADOW_MODE_RAY_TRACED)
            {
                // TODO: Implement
            }

            contribution *= (1.0 - shadow);
        }

        directLighting += contribution;
    }

    vec3 indirectLight;
    if (IsVXGI)
    {
        vec3 viewDir = fragPos - perFrameDataUBO.ViewPos;
        indirectLight = IndirectLight(surface, SamplerVoxels, fragPos, viewDir, settingsUBO.ConeTraceSettings) * surface.Albedo;
    }
    else
    {
        const vec3 ambient = vec3(0.015);
        indirectLight = ambient * surface.Albedo;
    }

    OutFragColor = vec4((directLighting + indirectLight) + surface.Emissive, surface.Alpha);
}
