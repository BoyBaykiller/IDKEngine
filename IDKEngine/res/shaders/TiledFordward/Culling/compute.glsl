#version 460 core
#define FLOAT_MAX 3.4028235e+38
#define FLOAT_MIN -3.4028235e+38
#define TILE_SIZE 16

layout(local_size_x = TILE_SIZE, local_size_y = TILE_SIZE, local_size_z = 1) in;

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    float _pad0;
};

struct Frustum
{
    vec4 Planes[6];
};

layout(std430, binding = 5) restrict readonly buffer TileLightIndicesSSBO
{
    uint Indices[];
} tileLightIndicesSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FreezeFramesCounter;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
} basicDataUBO;

layout(std140, binding = 3) uniform LightsUBO
{
    #define GLSL_MAX_UBO_LIGHT_COUNT 256 // used in shader and client code - keep in sync!
    Light Lights[GLSL_MAX_UBO_LIGHT_COUNT];
    int Count;
} lightsUBO;

layout(binding = 0) uniform sampler2D SamplerDepth;

shared uint SharedMinDepth;
shared uint SharedMaxDepth;
shared Frustum SharedFrustum;

Frustum CreateFrustum(vec2 negativeStep, vec2 positiveStep, float minDepth, float maxDepth);

void main()
{
    if (gl_LocalInvocationIndex == 0)
    {
        SharedMinDepth = FLOAT_MAX;
        SharedMaxDepth = FLOAT_MIN;
    }
    barrier();

    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgShaded);

    float depth = texture(SamplerDepth, uv).r;
    // linearize depth
    depth = (0.5 * basicDataUBO.Projection[3][2]) / (depth + 0.5 * basicDataUBO.Projection[2][2] - 0.5);

    atomicMin(SharedMinDepth, floatBitsToUint(depth));
    atomicMax(SharedMaxDepth, floatBitsToUint(depth));

    barrier();

    if (gl_LocalInvocationIndex == 0)
    {
        float minDepth = uintBitsToFloat(SharedMinDepth);
        float maxDepth = uintBitsToFloat(SharedMaxDepth);

        // TODO: Rework when properly implemented
        vec2 negativeStep = (2.0 * gl_WorkGroupID.xy) / gl_NumWorkGroups.xy;
		vec2 positiveStep = (2.0 * vec2(gl_WorkGroupID.xy + ivec2(1, 1))) / gl_NumWorkGroups.xy;

        SharedFrustum = CreateFrustum(negativeStep, positiveStep, minDepth, maxDepth);
    }

    barrier();
}

// Source: https://github.com/bcrusco/Forward-Plus-Renderer/blob/master/Forward-Plus/Forward-Plus/source/shaders/light_culling.comp.glsl#L84
Frustum CreateFrustum(vec2 negativeStep, vec2 positiveStep, float minDepth, float maxDepth)
{
    Frustum frustum;
    frustum.Planes[0] = vec4(1.0, 0.0, 0.0, 1.0 - negativeStep.x); // Left
    frustum.Planes[1] = vec4(-1.0, 0.0, 0.0, -1.0 + positiveStep.x); // Right
    frustum.Planes[2] = vec4(0.0, 1.0, 0.0, 1.0 - negativeStep.y); // Bottom
    frustum.Planes[3] = vec4(0.0, -1.0, 0.0, -1.0 + positiveStep.y); // Top
    frustum.Planes[4] = vec4(0.0, 0.0, -1.0, -minDepth); // Near
    frustum.Planes[5] = vec4(0.0, 0.0, 1.0, maxDepth); // Far

    for (uint i = 0; i < 4; i++) {
        frustum.Planes[i] *= viewProjection;
        frustum.Planes[i] /= length(frustum.Planes[i].xyz);
    }

    frustum.Planes[4] *= view;
    frustum.Planes[4] /= length(frustum.Planes[4].xyz);
    frustum.Planes[5] *= view;
    frustum.Planes[5] /= length(frustum.Planes[5].xyz);
}