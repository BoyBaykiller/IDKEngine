#ifndef TraceCone_H
#define TraceCone_H

AppInclude(include/Ray.glsl)

vec4 TraceCone(Ray ray, vec3 normal, float coneAngle, float stepMultiplier, float normalRayOffset, float alphaThreshold)
{
    vec3 voxelGridWorldSpaceSize = voxelizerDataUBO.GridMax - voxelizerDataUBO.GridMin;
    vec3 voxelWorldSpaceSize = voxelGridWorldSpaceSize / textureSize(SamplerVoxelsAlbedo, 0);
    float voxelMaxLength = max(voxelWorldSpaceSize.x, max(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    float voxelMinLength = min(voxelWorldSpaceSize.x, min(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    uint maxLevel = textureQueryLevels(SamplerVoxelsAlbedo) - 1;
    vec4 accumulatedColor = vec4(0.0);

    ray.Origin += normal * voxelMaxLength * normalRayOffset;

    float distFromStart = voxelMaxLength;
    while (accumulatedColor.a < alphaThreshold)
    {
        float coneDiameter = 2.0 * tan(coneAngle) * distFromStart;
        float sampleDiameter = max(voxelMinLength, coneDiameter);
        float sampleLod = log2(sampleDiameter / voxelMinLength);
        
        vec3 worldPos = ray.Origin + ray.Direction * distFromStart;
        vec3 ndc = (voxelizerDataUBO.OrthoProjection * vec4(worldPos, 1.0)).xyz;
        vec3 sampleUVW = ndc * 0.5 + 0.5;
        if (any(lessThan(sampleUVW, vec3(0.0))) || any(greaterThanEqual(sampleUVW, vec3(1.0))) || sampleLod > maxLevel)
        {
            break;
        }
        vec4 newSample = textureLod(SamplerVoxelsAlbedo, sampleUVW, sampleLod);

        float weightOfNewSample = (1.0 - accumulatedColor.a);
        accumulatedColor += weightOfNewSample * newSample;
        
        distFromStart += sampleDiameter * stepMultiplier;
    }

    return accumulatedColor;
}

vec4 TraceCone(Ray ray, float coneAngle, float stepMultiplier)
{
    const vec3 normal = vec3(0.0);
    const float normalRayOffset = 0.0;
    return TraceCone(ray, normal, coneAngle, stepMultiplier, normalRayOffset, 1.0);
}

#endif