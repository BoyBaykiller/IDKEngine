#version 460 core
#extension GL_AMD_gpu_shader_half_float : enable
#extension GL_AMD_gpu_shader_half_float_fetch : enable

#if GL_AMD_gpu_shader_half_float_fetch
#define MATERIAL_SAMPLER_2D_TYPE f16sampler2D
#endif

AppInclude(include/Ray.glsl)
AppInclude(include/Sampling.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(PathTracing/include/Constants.glsl)

#define TRAVERSAL_STACK_USE_SHARED_STACK_SIZE N_HIT_PROGRAM_LOCAL_SIZE_X
AppInclude(include/BVHIntersect.glsl)

layout(local_size_x = N_HIT_PROGRAM_LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

layout(std140, binding = 0) uniform SettingsUBO
{
    float FocalLength;
    float LenseRadius;
    bool IsDebugBVHTraversal;
    bool DoTraceLights;
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

        uvec3 keys = FloatToKey(wavefrontRay.Origin);
        atomicMin(wavefrontPTSSBO.RayBoundsMin.x, keys.x);
        atomicMin(wavefrontPTSSBO.RayBoundsMin.y, keys.y);
        atomicMin(wavefrontPTSSBO.RayBoundsMin.z, keys.z);
        atomicMax(wavefrontPTSSBO.RayBoundsMax.x, keys.x);
        atomicMax(wavefrontPTSSBO.RayBoundsMax.y, keys.y);
        atomicMax(wavefrontPTSSBO.RayBoundsMax.z, keys.z);
    }
}

bool TraceRay(inout GpuWavefrontRay wavefrontRay)
{
    vec3 rayDir = DecodeUnitVec(vec2(wavefrontRay.PackedDirectionX, wavefrontRay.PackedDirectionY));

    HitInfo hitInfo;
    if (TraceRay(Ray(wavefrontRay.Origin, rayDir), hitInfo, settingsUBO.DoTraceLights, FLOAT_MAX))
    {
        wavefrontRay.Origin += rayDir * hitInfo.T;

        Surface surface = GetDefaultSurface();
        vec3 geometricNormal;
        bool hitLight = hitInfo.TriangleId == ~0u;
        if (!hitLight)
        {
            uvec3 indices = Unpack(blasTriangleIndicesSSBO.Indices[hitInfo.TriangleId]);
            GpuVertex v0 = vertexSSBO.Vertices[indices.x];
            GpuVertex v1 = vertexSSBO.Vertices[indices.y];
            GpuVertex v2 = vertexSSBO.Vertices[indices.z];

            vec3 bary = vec3(hitInfo.BaryXY.xy, 1.0 - hitInfo.BaryXY.x - hitInfo.BaryXY.y);
            vec2 interpTexCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, bary);
            vec3 interpNormal = normalize(Interpolate(DecompressSR11G11B10(v0.Normal), DecompressSR11G11B10(v1.Normal), DecompressSR11G11B10(v2.Normal), bary));
            vec3 interpTangent = normalize(Interpolate(DecompressSR11G11B10(v0.Tangent), DecompressSR11G11B10(v1.Tangent), DecompressSR11G11B10(v2.Tangent), bary));

            GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[hitInfo.InstanceId];
            GpuMesh mesh = meshSSBO.Meshes[meshInstance.MeshId];
            GpuMaterial material = materialSSBO.Materials[mesh.MaterialId];

            surface = GetSurface(material, interpTexCoord);
            SurfaceApplyModificatons(surface, mesh);

            float alphaCutoff = SurfaceHasAlphaBlending(surface) ? GetRandomFloat01() : surface.AlphaCutoff;
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

            vec3 p0 = Unpack(vertexPositionsSSBO.Positions[indices.x]);
            vec3 p1 = Unpack(vertexPositionsSSBO.Positions[indices.y]);
            vec3 p2 = Unpack(vertexPositionsSSBO.Positions[indices.z]);
            geometricNormal = GetTriangleNormal(p0, p1, p2);
            geometricNormal = normalize(unitVecToWorld * geometricNormal);
        }
        else if (settingsUBO.DoTraceLights)
        {
            GpuLight light = lightsUBO.Lights[hitInfo.InstanceId];
            surface.Emissive = light.Color;
            surface.Albedo = light.Color;
            surface.Normal = (wavefrontRay.Origin - light.Position) / light.Radius;
            geometricNormal = surface.Normal;
        }

        bool fromInside = dot(-rayDir, geometricNormal) < 0.0;
        if (fromInside)
        {
            geometricNormal *= -1.0;
            
            if (surface.IsVolumetric)
            {
                wavefrontRay.Throughput *= exp(-surface.Absorbance * hitInfo.T);
            }
        }

        float cosTheta = dot(-rayDir, surface.Normal);
        if (cosTheta < 0.0)
        {
            surface.Normal *= -1.0;
            cosTheta *= -1.0;
        }
        cosTheta = min(cosTheta, 1.0);

        wavefrontRay.Radiance += surface.Emissive * wavefrontRay.Throughput;

        SampleMaterialResult result = SampleMaterial(rayDir, surface, wavefrontRay.PreviousIOROrTraverseCost, fromInside);
        wavefrontRay.Throughput *= result.Bsdf / result.Pdf;

        bool terminateRay = RussianRouletteTerminateRay(wavefrontRay.Throughput);
        if (terminateRay)
        {
            return false;
        }

        if (result.BsdfType == ENUM_BSDF_TRANSMISSIVE)
        {
            geometricNormal *= -1.0;   
        }
        wavefrontRay.Origin += geometricNormal * 0.001;
        wavefrontRay.PreviousIOROrTraverseCost = result.NewIor;

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
