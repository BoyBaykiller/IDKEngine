#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxels;

struct Ray
{
    vec3 Origin;
    vec3 Direction;
};

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    float _pad0;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 5) uniform VXGIDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} vxgiDataUBO;

bool RayCuboidIntersect(Ray ray, vec3 min, vec3 max, out float t1, out float t2);
vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords);

layout(location = 0) uniform int Steps;
layout(location = 1) uniform int Lod;

void main()
{
    ivec2 imgResultSize = imageSize(ImgResult);
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 ndc = (imgCoord + 0.5) / imgResultSize * 2.0 - 1.0;

    Ray worldRay;
    worldRay.Origin = basicDataUBO.ViewPos;
    worldRay.Direction = GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc);

    float t1, t2;
    if (!(RayCuboidIntersect(worldRay, vxgiDataUBO.GridMin, vxgiDataUBO.GridMax, t1, t2) && t2 > 0.0))
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }

    vec3 gridRayStart, gridRayEnd;
    bool isInsideGrid = t1 < 0.0 && t2 > 0.0;
    if (isInsideGrid)
        gridRayStart = basicDataUBO.ViewPos;
    else
        gridRayStart = worldRay.Origin + worldRay.Direction * t1;
    gridRayEnd = (worldRay.Origin + worldRay.Direction * t2);

    vec3 uvtStart = (vxgiDataUBO.OrthoProjection * vec4(gridRayStart, 1.0)).xyz * 0.5 + 0.5;
    vec3 uvtEnd = (vxgiDataUBO.OrthoProjection * vec4(gridRayEnd, 1.0)).xyz * 0.5 + 0.5;
    vec3 deltaStep = (uvtEnd - uvtStart) / Steps;
    vec3 currentPos = uvtStart;
    vec4 color = vec4(0.0);
    for (int i = 0; i < Steps; i++)
    {
        vec4 currSample = textureLod(SamplerVoxels, currentPos, Lod);
        color += (1.0 - color.a) * currSample;
        currentPos += deltaStep;
    }

    color.rgb = pow(color.rgb, vec3(2.2));
    imageStore(ImgResult, imgCoord, color);
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