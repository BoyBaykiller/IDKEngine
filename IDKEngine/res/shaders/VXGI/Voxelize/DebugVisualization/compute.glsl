#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxelsAlbedo;

struct Ray
{
    vec3 Origin;
    vec3 Direction;
};

AppInclude(shaders/include/Buffers.glsl)

vec4 TraceCone(vec3 start, vec3 direction, float coneAngle, float stepMultiplier);
bool RayCuboidIntersect(Ray ray, vec3 min, vec3 max, out float t1, out float t2);
vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords);

layout(location = 0) uniform float StepMultiplier;
layout(location = 1) uniform float ConeAngle;

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 ndc = (imgCoord + 0.5) / imageSize(ImgResult) * 2.0 - 1.0;

    Ray worldRay;
    worldRay.Origin = basicDataUBO.ViewPos;
    worldRay.Direction = GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc);

    float t1, t2;
    if (!(RayCuboidIntersect(worldRay, voxelizerDataUBO.GridMin, voxelizerDataUBO.GridMax, t1, t2) && t2 > 0.0))
    {
        vec4 skyColor = texture(skyBoxUBO.Albedo, worldRay.Direction);
        imageStore(ImgResult, imgCoord, skyColor);
        return;
    }

    vec3 gridRayStart;
    bool isInsideGrid = t1 < 0.0 && t2 > 0.0;
    if (isInsideGrid)
    {
        gridRayStart = basicDataUBO.ViewPos;
    }
    else
    {
        gridRayStart = worldRay.Origin + worldRay.Direction * t1;
    }

    vec4 color = TraceCone(gridRayStart, worldRay.Direction, ConeAngle, StepMultiplier);
    color += (1.0 - color.a) * (texture(skyBoxUBO.Albedo, worldRay.Direction));

    imageStore(ImgResult, imgCoord, color);
}

vec4 TraceCone(vec3 start, vec3 direction, float coneAngle, float stepMultiplier)
{
    vec3 voxelGridWorldSpaceSize = voxelizerDataUBO.GridMax - voxelizerDataUBO.GridMin;
    vec3 voxelWorldSpaceSize = voxelGridWorldSpaceSize / textureSize(SamplerVoxelsAlbedo, 0);
    float voxelMaxLength = max(voxelWorldSpaceSize.x, max(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    float voxelMinLength = min(voxelWorldSpaceSize.x, min(voxelWorldSpaceSize.y, voxelWorldSpaceSize.z));
    uint maxLevel = textureQueryLevels(SamplerVoxelsAlbedo) - 1;
    vec4 accumlatedColor = vec4(0.0);

    float distFromStart = voxelMaxLength;
    while (accumlatedColor.a < 1.0)
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

// Source: https://medium.com/@bromanz/another-view-on-the-classic-ray-aabb-intersection-algorithm-for-bvh-traversal-41125138b525
bool RayCuboidIntersect(Ray ray, vec3 aabbMin, vec3 aabbMax, out float t1, out float t2)
{
    t1 = FLOAT_MIN;
    t2 = FLOAT_MAX;

    vec3 t0s = (aabbMin - ray.Origin) / ray.Direction;
    vec3 t1s = (aabbMax - ray.Origin) / ray.Direction;

    vec3 tsmaller = min(t0s, t1s);
    vec3 tbigger = max(t0s, t1s);

    t1 = max(t1, max(tsmaller.x, max(tsmaller.y, tsmaller.z)));
    t2 = min(t2, min(tbigger.x, min(tbigger.y, tbigger.z)));

    return t1 <= t2;
}

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords)
{
    vec4 rayEye = inverseProj * vec4(normalizedDeviceCoords, -1.0, 0.0);
    rayEye.zw = vec2(-1.0, 0.0);
    return normalize((inverseView * rayEye).xyz);
}