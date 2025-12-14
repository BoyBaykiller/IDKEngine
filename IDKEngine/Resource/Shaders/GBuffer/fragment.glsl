#version 460 core

AppInclude(include/Surface.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(location = 0) out vec4 OutAlbedoAlpha;
layout(location = 1) out vec2 OutNormal;
layout(location = 2) out vec2 OutMetallicRoughness;
layout(location = 3) out vec3 OutEmissive;
layout(location = 4) out vec2 OutVelocity;

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
    GpuMesh mesh = meshSSBO.Meshes[inData.MeshId];
    GpuMaterial material = materialSSBO.Materials[mesh.MaterialId];
    
    Surface surface = GetSurface(material, inData.TexCoord, taaDataUBO.MipmapBias);
    SurfaceApplyModificatons(surface, mesh);

    if (surface.Alpha < surface.AlphaCutoff)
    {
        discard;
    }
    
    vec3 interpTangent = normalize(inData.Tangent);
    vec3 interpNormal = normalize(inData.Normal);
    mat3 tbn = GetTBN(interpTangent, interpNormal);
    surface.Normal = tbn * surface.Normal;
    surface.Normal = normalize(mix(interpNormal, surface.Normal, mesh.NormalMapStrength));

    OutAlbedoAlpha = vec4(surface.Albedo, 1.0);
    OutNormal = EncodeUnitVec(surface.Normal);
    OutMetallicRoughness = vec2(surface.Metallic, surface.Roughness);
    OutEmissive = surface.Emissive;

    vec2 uv = gl_FragCoord.xy / textureSize(gBufferDataUBO.Velocity, 0);
    vec2 thisNdc = (uv * 2.0 - 1.0) - taaDataUBO.Jitter;
    vec2 historyNdc = inData.PrevClipPos.xy / inData.PrevClipPos.w;
    OutVelocity = (thisNdc - historyNdc) * 0.5; // transformed to UV space [0, 1], + 0.5 cancels out
}