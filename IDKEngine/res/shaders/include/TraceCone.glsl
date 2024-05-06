AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Ray.glsl)
AppInclude(include/Transformations.glsl)

vec4 TraceCone(sampler3D samplerVoxels, Ray ray, vec3 normal, float coneAngle, float stepMultiplier, float normalRayOffset, float alphaThreshold)
{
    vec3 voxelGridWorldSpaceSize = voxelizerDataUBO.GridMax - voxelizerDataUBO.GridMin;
    vec3 voxelWorldSpaceSize = voxelGridWorldSpaceSize / textureSize(samplerVoxels, 0);
    float voxelMaxLength = max(voxelWorldSpaceSize.x, max(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    float voxelMinLength = min(voxelWorldSpaceSize.x, min(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    uint maxLevel = textureQueryLevels(samplerVoxels) - 1;
    vec4 accumulatedColor = vec4(0.0);

    ray.Origin += normal * voxelMaxLength * normalRayOffset;

    float distFromStart = voxelMaxLength;
    while (accumulatedColor.a < alphaThreshold)
    {
        float coneDiameter = 2.0 * tan(coneAngle) * distFromStart;
        float sampleDiameter = max(voxelMinLength, coneDiameter);
        float sampleLod = log2(sampleDiameter / voxelMinLength);
        
        vec3 worldPos = ray.Origin + ray.Direction * distFromStart;
        vec3 sampleUVW = MapToZeroOne(worldPos, voxelizerDataUBO.GridMin, voxelizerDataUBO.GridMax);
        if (any(lessThan(sampleUVW, vec3(0.0))) || any(greaterThanEqual(sampleUVW, vec3(1.0))) || sampleLod > maxLevel)
        {
            break;
        }
        vec4 newSample = textureLod(samplerVoxels, sampleUVW, sampleLod);

        // glBlendEquation(mode: GL_FUNC_ADD)
        // glBlendFunc(sfactor: GL_ONE_MINUS_DST_ALPHA, dfactor: 1.0)
        float weightOfNewSample = (1.0 - accumulatedColor.a);
        accumulatedColor += weightOfNewSample * newSample;
        
        distFromStart += sampleDiameter * stepMultiplier;
    }

    return accumulatedColor;
}

vec4 TraceCone(sampler3D samplerVoxels, Ray ray, float coneAngle, float stepMultiplier)
{
    const vec3 normal = vec3(0.0);
    const float normalRayOffset = 0.0;
    return TraceCone(samplerVoxels, ray, normal, coneAngle, stepMultiplier, normalRayOffset, 1.0);
}
