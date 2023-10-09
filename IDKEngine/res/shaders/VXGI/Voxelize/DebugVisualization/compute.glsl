#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX
#extension GL_ARB_bindless_texture : require

AppInclude(include/Transformations.glsl)
AppInclude(include/IntersectionRoutines.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxelsAlbedo;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    uint Frame;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

layout(std140, binding = 5) uniform VoxelizerDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} voxelizerDataUBO;


layout(location = 0) uniform float StepMultiplier;
layout(location = 1) uniform float ConeAngle;

AppInclude(VXGI/include/TraceCone.glsl)

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 ndc = (imgCoord + 0.5) / imageSize(ImgResult) * 2.0 - 1.0;

    Ray worldRay;
    worldRay.Origin = basicDataUBO.ViewPos;
    worldRay.Direction = GetWorldSpaceDirection(basicDataUBO.InvProjection, basicDataUBO.InvView, ndc);

    float t1, t2;
    if (!(RayBoxIntersect(worldRay, voxelizerDataUBO.GridMin, voxelizerDataUBO.GridMax, t1, t2) && t2 > 0.0))
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
