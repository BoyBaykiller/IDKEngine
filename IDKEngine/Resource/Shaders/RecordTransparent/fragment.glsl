#version 460 core

#define TRANSPARENT_LAYERS AppInsert(TRANSPARENT_LAYERS)

AppInclude(include/Math.glsl)
AppInclude(include/Sampling.glsl)
AppInclude(include/Surface.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(VXGI/ConeTraceGI/include/Impl.glsl)
AppInclude(DeferredLighting/include/Impl.glsl)

// Incoherent memory operations such as image access in this shader
// disable early fragment tests, but we can explicit re-enable it.
layout(early_fragment_tests) in;

layout(binding = 0) uniform sampler3D SamplerVoxels;
layout(binding = 0) restrict writeonly uniform image2DArray ImgRecordedColors;
layout(binding = 1) restrict writeonly uniform image2DArray ImgRecordedDepths;
layout(binding = 2, r32ui) restrict uniform uimage2D ImgRecordedFragmentsCounter;

layout(std140, binding = 0) uniform SettingsUBO
{
    ConeTraceGISettings ConeTraceSettings;
} settingsUBO;

uniform bool IsVXGI;
uniform ENUM_SHADOW_MODE ShadowMode;

in InOutData
{
    vec2 TexCoord;
    vec4 PrevClipPos;
    vec3 Normal;
    vec3 Tangent;
    flat uint MeshId;
} inData;

void main()
{
    ivec2 imgCoord = ivec2(gl_FragCoord.xy);

    GpuMesh mesh = meshSSBO.Meshes[inData.MeshId];
    GpuMaterial material = materialSSBO.Materials[mesh.MaterialId];
    
    Surface surface = GetSurface(material, inData.TexCoord, taaDataUBO.MipmapBias);
    SurfaceApplyModificatons(surface, mesh);

    if (surface.Alpha == 0.0)
    {
        return;
    }

    uint fragmentIndex = imageAtomicAdd(ImgRecordedFragmentsCounter, imgCoord, 1u);
    if (fragmentIndex >= TRANSPARENT_LAYERS)
    {
        return;
    }

    vec3 interpTangent = normalize(inData.Tangent);
    vec3 interpNormal = normalize(inData.Normal);
    mat3 tbn = GetTBN(interpTangent, interpNormal);
    surface.Normal = tbn * surface.Normal;
    surface.Normal = normalize(mix(interpNormal, surface.Normal, mesh.NormalMapStrength));

    if (!gl_FrontFacing)
    {
        // For doubleSided materials the back-face MUST have its normals reversed before the lighting equation is evaluated
        surface.Normal = -surface.Normal;
    }

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
            else if (ShadowMode == ENUM_SHADOW_MODE_PCF)
            {
                GpuPointShadow pointShadow = shadowsUBO.PointShadows[light.PointShadowIndex];
                vec3 lightToSample = unjitteredFragPos - light.Position;
                shadow = 1.0 - Visibility(pointShadow, surface.Normal, lightToSample);
            }
            else if (ShadowMode == ENUM_SHADOW_MODE_RAY_TRACED)
            {
                // TODO: Implement ray traced shadows for transparents?
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

    vec4 colorPremult = vec4((directLighting + indirectLight) + surface.Emissive, surface.Alpha);
    colorPremult.rgb *= colorPremult.a;

    imageStore(ImgRecordedColors, ivec3(imgCoord, fragmentIndex), colorPremult);
    imageStore(ImgRecordedDepths, ivec3(imgCoord, fragmentIndex), vec4(gl_FragCoord.z));
}
