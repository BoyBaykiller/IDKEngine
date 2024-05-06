#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -FLOAT_MAX

AppInclude(include/Transformations.glsl)
AppInclude(include/TraceCone.glsl)
AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxels;

layout(location = 0) uniform float StepMultiplier;
layout(location = 1) uniform float ConeAngle;


void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 ndc = (imgCoord + 0.5) / imageSize(ImgResult) * 2.0 - 1.0;

    Ray worldRay;
    worldRay.Origin = perFrameDataUBO.ViewPos;
    worldRay.Direction = GetWorldSpaceDirection(perFrameDataUBO.InvProjection, perFrameDataUBO.InvView, ndc);

    float t1, t2;
    if (!(RayBoxIntersect(worldRay, Box(voxelizerDataUBO.GridMin, voxelizerDataUBO.GridMax), t1, t2) && t2 > 0.0))
    {
        vec4 skyColor = texture(skyBoxUBO.Albedo, worldRay.Direction);
        imageStore(ImgResult, imgCoord, skyColor);
        return;
    }

    bool isInsideGrid = t1 < 0.0 && t2 > 0.0;
    if (isInsideGrid)
    {
        worldRay.Origin = perFrameDataUBO.ViewPos;
    }
    else
    {
        worldRay.Origin = worldRay.Origin + worldRay.Direction * t1;
    }

    vec4 color = TraceCone(SamplerVoxels, worldRay, ConeAngle, StepMultiplier);
    color += (1.0 - color.a) * texture(skyBoxUBO.Albedo, worldRay.Direction);

    imageStore(ImgResult, imgCoord, color);
}
