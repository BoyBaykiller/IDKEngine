#version 460 core
#extension GL_AMD_gpu_shader_half_float : enable
#extension GL_AMD_gpu_shader_half_float_fetch : enable // requires GL_AMD_gpu_shader_half_float

#if GL_AMD_gpu_shader_half_float_fetch
#define MATERIAL_SAMPLER_2D_TYPE f16sampler2D
#else
#define MATERIAL_SAMPLER_2D_TYPE sampler2D
#endif

#define DECLARE_BVH_TRAVERSAL_STORAGE_BUFFERS
AppInclude(include/StaticStorageBuffers.glsl)

AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Random.glsl)
AppInclude(include/Ray.glsl)

AppInclude(PathTracing/include/Constants.glsl)

#define LOCAL_SIZE_X 8
#define LOCAL_SIZE_Y 8
#define TRAVERSAL_STACK_USE_SHARED_STACK_SIZE LOCAL_SIZE_X * LOCAL_SIZE_Y
AppInclude(include/BVHIntersect.glsl)

layout(local_size_x = LOCAL_SIZE_X, local_size_y = LOCAL_SIZE_Y, local_size_z = 1) in;

layout(binding = 0) restrict readonly writeonly uniform image2D ImgResult;

layout(std140, binding = 7) uniform SettingsUBO
{
    float FocalLength;
    float LenseRadius;
    bool IsDebugBVHTraversal;
    bool IsTraceLights;
    bool IsAlwaysTintWithAlbedo;
} settingsUBO;

bool TraceRay(inout WavefrontRay wavefrontRay);
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
    vec3 pointOnLense = (perFrameDataUBO.InvView * vec4(settingsUBO.LenseRadius * UniformSampleDisk(), 0.0, 1.0)).xyz;

    camDir = normalize(focalPoint - pointOnLense);

    WavefrontRay wavefrontRay;
    wavefrontRay.Origin = pointOnLense;
    
    vec2 compressedDir = EncodeUnitVec(camDir);
    wavefrontRay.CompressedDirectionX = compressedDir.x;
    wavefrontRay.CompressedDirectionY = compressedDir.y;

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

bool TraceRay(inout WavefrontRay wavefrontRay)
{
    vec3 uncompressedDir = DecodeUnitVec(vec2(wavefrontRay.CompressedDirectionX, wavefrontRay.CompressedDirectionY));

    HitInfo hitInfo;
    uint debugNodeCounter = 0;
    if (TraceRay(Ray(wavefrontRay.Origin, uncompressedDir), hitInfo, debugNodeCounter, settingsUBO.IsTraceLights, FLOAT_MAX))
    {
        if (settingsUBO.IsDebugBVHTraversal)
        {
            wavefrontRay.PreviousIOROrDebugNodeCounter = debugNodeCounter;
            return false;
        }

        wavefrontRay.Origin += uncompressedDir * hitInfo.T;

        vec3 albedo = vec3(0.0);
        vec3 normal = vec3(0.0);
        vec3 emissive = vec3(0.0);
        float transmissionChance = 0.0;
        float specularChance = 0.0;
        float roughness = 0.0;
        float ior = 1.0;
        vec3 absorbance = vec3(0.0);
        
        bool hitLight = hitInfo.VertexIndices == uvec3(0);
        if (!hitLight)
        {
            Vertex v0 = vertexSSBO.Vertices[hitInfo.VertexIndices.x];
            Vertex v1 = vertexSSBO.Vertices[hitInfo.VertexIndices.y];
            Vertex v2 = vertexSSBO.Vertices[hitInfo.VertexIndices.z];

            vec2 interpTexCoord = Interpolate(v0.TexCoord, v1.TexCoord, v2.TexCoord, hitInfo.Bary);
            vec3 interpNormal = normalize(Interpolate(DecompressSR11G11B10(v0.Normal), DecompressSR11G11B10(v1.Normal), DecompressSR11G11B10(v2.Normal), hitInfo.Bary));
            vec3 interpTangent = normalize(Interpolate(DecompressSR11G11B10(v0.Tangent), DecompressSR11G11B10(v1.Tangent), DecompressSR11G11B10(v2.Tangent), hitInfo.Bary));

            MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[hitInfo.InstanceID];
            Mesh mesh = meshSSBO.Meshes[meshInstance.MeshIndex];
            Material material = materialSSBO.Materials[mesh.MaterialIndex];

            mat3 unitVecToWorld = mat3(transpose(meshInstance.InvModelMatrix));
            vec3 worldNormal = normalize(unitVecToWorld * interpNormal);
            vec3 worldTangent = normalize(unitVecToWorld * interpTangent);
            mat3 TBN = GetTBN(worldTangent, worldNormal);
            vec3 textureWorldNormal = texture(material.Normal, interpTexCoord).rgb;
            textureWorldNormal = TBN * (textureWorldNormal * 2.0 - 1.0);
            normal = normalize(mix(worldNormal, textureWorldNormal, mesh.NormalMapStrength));

            ior = max(material.IOR + mesh.IORBias, 1.0);
            vec4 albedoAlpha = texture(material.BaseColor, interpTexCoord) * DecompressUR8G8B8A8(material.BaseColorFactor);
            albedo = albedoAlpha.rgb;
            absorbance = max(mesh.AbsorbanceBias + material.Absorbance, vec3(0.0));
            emissive = texture(material.Emissive, interpTexCoord).rgb * material.EmissiveFactor * MATERIAL_EMISSIVE_FACTOR + mesh.EmissiveBias * albedo;
            
            transmissionChance = clamp(texture(material.Transmission, interpTexCoord).r * material.TransmissionFactor + mesh.TransmissionBias, 0.0, 1.0);
            roughness = clamp(texture(material.MetallicRoughness, interpTexCoord).g * material.RoughnessFactor + mesh.RoughnessBias, 0.0, 1.0);
            specularChance = clamp(texture(material.MetallicRoughness, interpTexCoord).r * material.MetallicFactor + mesh.SpecularBias, 0.0, 1.0 - transmissionChance);
            
            float alphaCutoff = material.DoAlphaBlending ? GetRandomFloat01() : material.AlphaCutoff;
            if (albedoAlpha.a < alphaCutoff)
            {
                wavefrontRay.Origin += uncompressedDir * 0.001;
                return true;
            }
        }
        else if (settingsUBO.IsTraceLights)
        {
            Light light = lightsUBO.Lights[hitInfo.InstanceID];
            emissive = light.Color;
            albedo = light.Color;
            normal = (wavefrontRay.Origin - light.Position) / light.Radius;
        }

        float cosTheta = dot(-uncompressedDir, normal);
        bool fromInside = cosTheta < 0.0;

        float prevIor = 1.0;
        if (fromInside)
        {
            // This is the first hit shader which means we determine previous IOR here
            prevIor = ior;
            
            normal *= -1.0;
            cosTheta *= -1.0;

            wavefrontRay.Throughput = ApplyAbsorption(wavefrontRay.Throughput, absorbance, hitInfo.T);
        }

        wavefrontRay.Radiance += emissive * wavefrontRay.Throughput;

        // specularChance = 0.0;
        // roughness = 0.0;
        // transmissionChance = 0.0;

        float diffuseChance = 1.0 - specularChance - transmissionChance;
        specularChance = SpecularBasedOnViewAngle(specularChance, cosTheta, prevIor, ior);
        transmissionChance = 1.0 - diffuseChance - specularChance; // normalize again to (diff + spec + trans == 1.0)

        NewRayProperties result = SampleMaterial(uncompressedDir, specularChance, roughness, transmissionChance, ior, prevIor, normal, fromInside);

        if (result.RayType != RAY_TYPE_REFRACTIVE || settingsUBO.IsAlwaysTintWithAlbedo)
        {
            vec3 brdf = albedo / PI;
            float pdf = max(cosTheta / PI, 0.0001);
            wavefrontRay.Throughput *= cosTheta * brdf / pdf;
            // wavefrontRay.Throughput *= albedo;
        }
        
        wavefrontRay.Throughput /= result.RayTypeProbability;

        bool terminateRay = RussianRouletteTerminateRay(wavefrontRay.Throughput);
        if (terminateRay)
        {
            return false;
        }

        wavefrontRay.Origin += result.Direction * 0.001;
        wavefrontRay.PreviousIOROrDebugNodeCounter = result.Ior;

        vec2 compressedDir = EncodeUnitVec(result.Direction);
        wavefrontRay.CompressedDirectionX = compressedDir.x;
        wavefrontRay.CompressedDirectionY = compressedDir.y;
        return true;
    }
    else
    {
        wavefrontRay.Radiance += texture(skyBoxUBO.Albedo, uncompressedDir).rgb * wavefrontRay.Throughput;
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