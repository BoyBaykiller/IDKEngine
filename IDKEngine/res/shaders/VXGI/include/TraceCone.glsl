#ifndef TraceCone_H
#define TraceCone_H

vec4 TraceCone(vec3 start, vec3 direction, vec3 normal, float coneAngle, float stepMultiplier, float normalRayOffset, float alphaThreshold)
{
    vec3 voxelGridWorldSpaceSize = voxelizerDataUBO.GridMax - voxelizerDataUBO.GridMin;
    vec3 voxelWorldSpaceSize = voxelGridWorldSpaceSize / textureSize(SamplerVoxelsAlbedo, 0);
    float voxelMaxLength = max(voxelWorldSpaceSize.x, max(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    float voxelMinLength = min(voxelWorldSpaceSize.x, min(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    uint maxLevel = textureQueryLevels(SamplerVoxelsAlbedo) - 1;
    vec4 accumlatedColor = vec4(0.0);

    start += normal * voxelMaxLength * normalRayOffset;

    float distFromStart = voxelMaxLength;
    while (accumlatedColor.a < alphaThreshold)
    {
        float coneDiameter = 2.0 * tan(coneAngle) * distFromStart;
        float sampleDiameter = max(voxelMinLength, coneDiameter);
        float sampleLod = log2(sampleDiameter / voxelMinLength);
        
        vec3 worldPos = start + direction * distFromStart;
        vec3 ndc = (voxelizerDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz;
        vec3 sampleUVW = NdcToUvDepth(ndc);
        if (any(lessThan(sampleUVW, vec3(0.0))) || any(greaterThanEqual(sampleUVW, vec3(1.0))) || sampleLod > maxLevel)
        {
            break;
        }
        vec4 sampleColor = textureLod(SamplerVoxelsAlbedo, sampleUVW, sampleLod);

        accumlatedColor += (1.0 - accumlatedColor.a) * sampleColor;
        distFromStart += sampleDiameter * stepMultiplier;
    }

    return accumlatedColor;
}

vec4 TraceCone(vec3 start, vec3 direction, float coneAngle, float stepMultiplier)
{
    const vec3 normal = vec3(0.0);
    const float normalRayOffset = 0.0;
    return TraceCone(start, direction, normal, coneAngle, stepMultiplier, normalRayOffset, 1.0);
}

#endif