vec4 TraceCone(vec3 start, vec3 direction, vec3 normal, float coneAngle, float stepMultiplier, float normalRayOffset)
{
    vec3 voxelGridWorldSpaceSize = voxelizerDataUBO.GridMax - voxelizerDataUBO.GridMin;
    vec3 voxelWorldSpaceSize = voxelGridWorldSpaceSize / textureSize(SamplerVoxelsAlbedo, 0);
    float voxelMaxLength = max(voxelWorldSpaceSize.x, max(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    float voxelMinLength = min(voxelWorldSpaceSize.x, min(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    uint maxLevel = textureQueryLevels(SamplerVoxelsAlbedo) - 1;
    vec4 accumlatedColor = vec4(0.0);

    start += normal * voxelMaxLength * normalRayOffset;

    float distFromStart = voxelMaxLength;
    while (accumlatedColor.a < 0.99)
    {
        float coneDiameter = 2.0 * tan(coneAngle) * distFromStart;
        float sampleDiameter = max(voxelMinLength, coneDiameter);
        float sampleLod = log2(sampleDiameter / voxelMinLength);
        
        vec3 worldPos = start + direction * distFromStart;
        vec3 sampleUVT = (voxelizerDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz * 0.5 + 0.5;
        if (any(lessThan(sampleUVT, vec3(0.0))) || any(greaterThanEqual(sampleUVT, vec3(1.0))) || sampleLod > maxLevel)
        {
            break;
        }
        vec4 sampleColor = textureLod(SamplerVoxelsAlbedo, sampleUVT, sampleLod);

        accumlatedColor += (1.0 - accumlatedColor.a) * sampleColor;
        distFromStart += sampleDiameter * stepMultiplier;
    }

    return accumlatedColor;
}

vec4 TraceCone(vec3 start, vec3 direction, float coneAngle, float stepMultiplier)
{
    const vec3 normal = vec3(0.0);
    const float normalRayOffset = 0.0;
    return TraceCone(start, direction, normal, coneAngle, stepMultiplier, normalRayOffset);
}

