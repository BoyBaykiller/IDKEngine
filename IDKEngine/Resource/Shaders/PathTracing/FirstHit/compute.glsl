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

#define LOCAL_SIZE_X 8
#define LOCAL_SIZE_Y 8
#define TRAVERSAL_STACK_USE_SHARED_STACK_SIZE LOCAL_SIZE_X * LOCAL_SIZE_Y
AppInclude(include/BVHIntersect.glsl)

layout(local_size_x = LOCAL_SIZE_X, local_size_y = LOCAL_SIZE_Y, local_size_z = 1) in;

layout(binding = 0) restrict readonly writeonly uniform image2D ImgResult;

layout(std140, binding = 0) uniform SettingsUBO
{
    float FocalLength;
    float LenseRadius;
    bool IsDebugBVHTraversal;
    bool IsTraceLights;
} settingsUBO;

bool TraceRay(inout GpuWavefrontRay wavefrontRay);
ivec2 ReorderInvocations(uint n);

AppInclude(PathTracing/include/RussianRoulette.glsl)
AppInclude(PathTracing/include/Shading.glsl)

void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ReorderInvocations(20);
    if (any(greaterThanEqual(imgCoord, imgResultSize)))
    {
        return;
    }

    InitializeRandomSeed((imgCoord.y * 4096 + imgCoord.x) * (wavefrontPTSSBO.AccumulatedSamples + 1));

    vec2 subPixelOffset = vec2(GetRandomFloat01(), GetRandomFloat01());
    vec2 ndc = (imgCoord + subPixelOffset) / imgResultSize * 2.0 - 1.0;

    vec3 camDir = GetWorldSpaceDirection(perFrameDataUBO.InvProjection, perFrameDataUBO.InvView, ndc);
    vec3 focalPoint = perFrameDataUBO.ViewPos + camDir * settingsUBO.FocalLength;
    vec3 pointOnLense = (perFrameDataUBO.InvView * vec4(settingsUBO.LenseRadius * SampleDisk(), 0.0, 1.0)).xyz;

    camDir = normalize(focalPoint - pointOnLense);

    GpuWavefrontRay wavefrontRay;
    wavefrontRay.Origin = pointOnLense;
    
    vec2 packedDir = EncodeUnitVec(camDir);
    wavefrontRay.PackedDirectionX = packedDir.x;
    wavefrontRay.PackedDirectionY = packedDir.y;

    wavefrontRay.Throughput = vec3(1.0);
    wavefrontRay.Radiance = vec3(0.0);
    wavefrontRay.PreviousIOROrDebugNodeCounter = 1.0;
    
    uint rayIndex = imgCoord.y * imageSize(ImgResult).x + imgCoord.x;

    bool continueRay = TraceRay(wavefrontRay);
    wavefrontRaySSBO.Rays[rayIndex] = wavefrontRay;

    if (continueRay)
    {
        uint index = atomicAdd(wavefrontPTSSBO.Counts[1], 1u);
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
    uint debugNodeCounter = 0;
    if (TraceRay(Ray(wavefrontRay.Origin, rayDir), hitInfo, debugNodeCounter, settingsUBO.IsTraceLights, FLOAT_MAX))
    {
        if (settingsUBO.IsDebugBVHTraversal)
        {
            wavefrontRay.PreviousIOROrDebugNodeCounter = debugNodeCounter;
            return false;
        }

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

            float alphaCutoff = SurfaceIsAlphaBlending(surface) ? GetRandomFloat01() : surface.AlphaCutoff;
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
        else if (settingsUBO.IsTraceLights)
        {
            GpuLight light = lightsUBO.Lights[hitInfo.InstanceId];
            surface.Emissive = light.Color;
            surface.Albedo = light.Color;
            surface.Normal = (wavefrontRay.Origin - light.Position) / light.Radius;
            geometricNormal = surface.Normal;
        }

        // This is the first hit shader so we determine previous IOR here
        float prevIor = 1.0;

        bool fromInside = dot(-rayDir, geometricNormal) < 0.0;
        if (fromInside)
        {
            prevIor = surface.IOR;

            geometricNormal *= -1.0;
            wavefrontRay.Throughput *= exp(-surface.Absorbance * hitInfo.T);
        }

        float cosTheta = dot(-rayDir, surface.Normal);
        if (cosTheta < 0.0)
        {
            surface.Normal *= -1.0;
            cosTheta *= -1.0;
        }
        cosTheta = min(cosTheta, 1.0);

        wavefrontRay.Radiance += surface.Emissive * wavefrontRay.Throughput;

        SampleMaterialResult result = SampleMaterial(rayDir, surface, prevIor, fromInside);
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

ivec2 ReorderInvocations(uint n)
{
    // Source: https://youtu.be/HgisCS30yAI
    // Info: https://developer.nvidia.com/blog/optimizing-compute-shaders-for-l2-locality-using-thread-group-id-swizzling/
    
    uint idx = gl_WorkGroupID.y * gl_NumWorkGroups.x + gl_WorkGroupID.x;

    uint columnSize = gl_NumWorkGroups.y * n;
    uint fullColumnCount = gl_NumWorkGroups.x / n;
    uint lastColumnWidth = gl_NumWorkGroups.x % n;

    uint columnIdx = idx / columnSize;
    uint idxInColumn = idx % columnSize;

    uint columnWidth = n;
    if (columnIdx == fullColumnCount)
    {
        columnWidth = lastColumnWidth;
    } 

    uvec2 workGroupSwizzled;
    workGroupSwizzled.y = idxInColumn / columnWidth;
    workGroupSwizzled.x = idxInColumn % columnWidth + columnIdx * n;

    ivec2 pos = ivec2(workGroupSwizzled * gl_WorkGroupSize.xy + gl_LocalInvocationID.xy);
    return pos;
}