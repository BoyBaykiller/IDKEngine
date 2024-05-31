#version 460 core
#extension GL_AMD_gpu_shader_half_float : enable
#extension GL_AMD_gpu_shader_half_float_fetch : enable

#if GL_AMD_gpu_shader_half_float_fetch
#define MATERIAL_SAMPLER_2D_TYPE f16sampler2D
#else
#define MATERIAL_SAMPLER_2D_TYPE sampler2D
#endif

#define DECLARE_BVH_TRAVERSAL_STORAGE_BUFFERS
AppInclude(include/StaticStorageBuffers.glsl)

AppInclude(include/Ray.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(PathTracing/include/Constants.glsl)

#define TRAVERSAL_STACK_USE_SHARED_STACK_SIZE N_HIT_PROGRAM_LOCAL_SIZE_X
AppInclude(include/BVHIntersect.glsl)

layout(local_size_x = N_HIT_PROGRAM_LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

layout(std140, binding = 7) uniform SettingsUBO
{
    float FocalLength;
    float LenseRadius;
    bool IsDebugBVHTraversal;
    bool IsTraceLights;
    bool IsAlwaysTintWithAlbedo;
} settingsUBO;

bool TraceRay(inout GpuWavefrontRay wavefrontRay);

AppInclude(PathTracing/include/RussianRoulette.glsl)
AppInclude(PathTracing/include/Shading.glsl)

void main()
{
    uint pingPongIndex = wavefrontPTSSBO.PingPongIndex;
    if (gl_GlobalInvocationID.x >= wavefrontPTSSBO.Counts[pingPongIndex])
    {
        return;
    }

    if (gl_GlobalInvocationID.x == 0)
    {
        // reset the arguments that were used to launch this compute shader
        atomicAdd(wavefrontPTSSBO.DispatchCommand.NumGroupsX, -int(gl_NumWorkGroups.x));
    }

    InitializeRandomSeed(gl_GlobalInvocationID.x * 4096 + wavefrontPTSSBO.AccumulatedSamples);

    uint rayIndex = wavefrontPTSSBO.AliveRayIndices[gl_GlobalInvocationID.x];
    GpuWavefrontRay wavefrontRay = wavefrontRaySSBO.Rays[rayIndex];
    
    bool continueRay = TraceRay(wavefrontRay);
    wavefrontRaySSBO.Rays[rayIndex] = wavefrontRay;

    if (continueRay)
    {
        uint index = atomicAdd(wavefrontPTSSBO.Counts[1 - pingPongIndex], 1u);
        wavefrontPTSSBO.AliveRayIndices[index] = rayIndex;

        if (index % N_HIT_PROGRAM_LOCAL_SIZE_X == 0)
        {
            atomicAdd(wavefrontPTSSBO.DispatchCommand.NumGroupsX, 1);
        }
    }
}

bool TraceRay(inout GpuWavefrontRay wavefrontRay)
{
    vec3 rayDir = DecodeUnitVec(vec2(wavefrontRay.PackedDirectionX, wavefrontRay.PackedDirectionY));

    HitInfo hitInfo;
    if (TraceRay(Ray(wavefrontRay.Origin, rayDir), hitInfo, settingsUBO.IsTraceLights, FLOAT_MAX))
    {
        wavefrontRay.Origin += rayDir * hitInfo.T;

        Surface surface = GetDefaultSurface();
        if (hitInfo.VertexIndices != uvec3(0))
        {
            GpuVertex v0 = vertexSSBO.Vertices[hitInfo.VertexIndices.x];
            GpuVertex v1 = vertexSSBO.Vertices[hitInfo.VertexIndices.y];
            GpuVertex v2 = vertexSSBO.Vertices[hitInfo.VertexIndices.z];

            vec2 interpTexCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);
            vec3 interpNormal = normalize(Interpolate(DecompressSR11G11B10(v0.Normal), DecompressSR11G11B10(v1.Normal), DecompressSR11G11B10(v2.Normal), hitInfo.Bary));
            vec3 interpTangent = normalize(Interpolate(DecompressSR11G11B10(v0.Tangent), DecompressSR11G11B10(v1.Tangent), DecompressSR11G11B10(v2.Tangent), hitInfo.Bary));

            GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[hitInfo.InstanceID];
            GpuMesh mesh = meshSSBO.Meshes[meshInstance.MeshIndex];
            GpuMaterial material = materialSSBO.Materials[mesh.MaterialIndex];

            surface = GetSurface(material, interpTexCoord);
            SurfaceApplyModificatons(surface, mesh);

            float alphaCutoff = surface.DoAlphaBlending ? GetRandomFloat01() : surface.AlphaCutoff;
            if (surface.Alpha < alphaCutoff)
            {
                wavefrontRay.Origin += rayDir * 0.001;
                return true;
            }

            mat3 unitVecToWorld = mat3(transpose(meshInstance.InvModelMatrix));
            vec3 worldNormal = normalize(unitVecToWorld * interpNormal);
            vec3 worldTangent = normalize(unitVecToWorld * interpTangent);
            mat3 tbn = GetTBN(worldTangent, worldNormal);
            surface.Normal = tbn * surface.Normal;
            surface.Normal = normalize(mix(worldNormal, surface.Normal, mesh.NormalMapStrength));
        }
        else if (settingsUBO.IsTraceLights)
        {
            GpuLight light = lightsUBO.Lights[hitInfo.InstanceID];
            surface.Emissive = light.Color;
            surface.Albedo = light.Color;
            surface.Normal = (wavefrontRay.Origin - light.Position) / light.Radius;
        }

        float cosTheta = dot(-rayDir, surface.Normal);
        bool fromInside = cosTheta < 0.0;

        if (fromInside)
        {
            surface.Normal *= -1.0;
            cosTheta *= -1.0;

            wavefrontRay.Throughput = ApplyAbsorption(wavefrontRay.Throughput, surface.Absorbance, hitInfo.T);
        }
        cosTheta = clamp(cosTheta, 0.0, 1.0);

        wavefrontRay.Radiance += surface.Emissive * wavefrontRay.Throughput;

        SampleMaterialResult result = SampleMaterial(rayDir, surface, wavefrontRay.PreviousIOROrDebugNodeCounter, fromInside);
        if (result.RayType != RAY_TYPE_REFRACTIVE || settingsUBO.IsAlwaysTintWithAlbedo)
        {
            wavefrontRay.Throughput *= result.Bsdf / result.Pdf * cosTheta;
        }
        wavefrontRay.Throughput /= result.RayTypeProbability;

        bool terminateRay = RussianRouletteTerminateRay(wavefrontRay.Throughput);
        if (terminateRay)
        {
            return false;
        }

        wavefrontRay.Origin += result.RayDirection * 0.001;
        wavefrontRay.PreviousIOROrDebugNodeCounter = result.NewIor;

        vec2 packedDir = EncodeUnitVec(result.RayDirection);
        wavefrontRay.PackedDirectionX = packedDir.x;
        wavefrontRay.PackedDirectionY = packedDir.y;
        return true;
    }
    else
    {
        wavefrontRay.Radiance += texture(skyBoxUBO.Albedo, rayDir).rgb * wavefrontRay.Throughput;
        return false;
    }
}
