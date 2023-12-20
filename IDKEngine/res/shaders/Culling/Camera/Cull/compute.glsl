#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/IntersectionRoutines.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

struct DrawElementsCmd
{
    uint Count;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;

    uint BlasRootNodeIndex;
};

struct Mesh
{
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    float _pad0;
    vec3 Absorbance;
    uint CubemapShadowCullInfo;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

struct BlasNode
{
    vec3 Min;
    uint TriStartOrLeftChild;
    vec3 Max;
    uint TriCount;
};

layout(std430, binding = 0) restrict buffer DrawElementsCmdSSBO
{
    DrawElementsCmd DrawCommands[];
} drawElementsCmdSSBO;

layout(std430, binding = 1) restrict readonly writeonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 5) restrict readonly buffer BlasSSBO
{
    BlasNode Nodes[];
} blasSSBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

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
    float DeltaRenderTime;
    float Time;
} basicDataUBO;

void main()
{
    uint meshIndex = gl_GlobalInvocationID.x;
    if (meshIndex >= meshSSBO.Meshes.length())
    {
        return;
    }

    DrawElementsCmd drawCmd = drawElementsCmdSSBO.DrawCommands[meshIndex];
    BlasNode node = blasSSBO.Nodes[drawCmd.BlasRootNodeIndex];

    const uint glInstanceID = 0; // TODO: Derive from built in variables
    mat4 model = meshInstanceSSBO.MeshInstances[drawCmd.BaseInstance + glInstanceID].PrevModelMatrix;

    bool isVisible = true;

    Frustum frustum = GetFrustum(basicDataUBO.ProjView * model);
    isVisible = FrustumBoxIntersect(frustum, node.Min, node.Max);

    // Occlusion cull
    const bool hiZCulling = false; // false for now
    if (hiZCulling && isVisible)
    {
        bool behindFrustum = false;
        vec2 boxNdcMin = vec2(FLOAT_MAX);
        vec2 boxNdcMax = vec2(FLOAT_MIN);
        float boxClosestDepth = FLOAT_MAX;
        {
            Box localBox = Box(node.Min, node.Max);
            for (int i = 0; i < 8; i++)
            {
                vec4 clipSpace = basicDataUBO.PrevProjView * model * vec4(BoxGetVertexPos(localBox, i), 1.0);
                if (clipSpace.w <= 0.0)
                {
                    behindFrustum = true;
                    break;
                }
                vec2 ndc = clipSpace.xy / clipSpace.w;
                boxNdcMin = min(boxNdcMin, ndc);
                boxNdcMax = max(boxNdcMax, ndc);

                float depth = clipSpace.z / clipSpace.w;
                boxClosestDepth = min(boxClosestDepth, depth);
            }
        }

        if (!behindFrustum)
        {
            vec2 boxUvMin = boxNdcMin * 0.5 + 0.5;
            vec2 boxUvMax = boxNdcMax * 0.5 + 0.5;

            boxUvMin = clamp(boxUvMin, vec2(0.0), vec2(1.0));
            boxUvMax = clamp(boxUvMax, vec2(0.0), vec2(1.0));

            sampler2D samplerHiZ = gBufferDataUBO.Depth;
            ivec2 size = ivec2((boxUvMax - boxUvMin) * textureSize(samplerHiZ, 0));
            uint level = uint(ceil(log2(max(size.x, size.y))));

            // Source: https://interplayoflight.wordpress.com/2017/11/15/experiments-in-gpu-based-occlusion-culling/
            // uint lowerLevel = max(level - 1, 0);
            // float scale = exp2(-float(lowerLevel));
            // ivec2 a = ivec2(floor(boxUvMin * scale));
            // ivec2 b = ivec2(ceil(boxUvMax * scale));
            // ivec2 dims = b - a;
            // // Use the lower level if we only touch <= 2 texels in both dimensions
            // if (dims.x <= 2 && dims.y <= 2)
            // {
            //     level = lowerLevel;
            // }

            vec4 depths;
            depths.x = textureLod(samplerHiZ, boxUvMin, level).r;
            depths.y = textureLod(samplerHiZ, vec2(boxUvMax.x, boxUvMin.y), level).r;
            depths.w = textureLod(samplerHiZ, vec2(boxUvMin.x, boxUvMax.y), level).r;
            depths.z = textureLod(samplerHiZ, boxUvMax, level).r;

            float furthestDepth = max(max(depths.x, depths.y), max(depths.z, depths.w));
            if (boxClosestDepth > furthestDepth)
            {
                isVisible = false;
            }
        }
    }
    
    drawElementsCmdSSBO.DrawCommands[meshIndex].InstanceCount = isVisible ? 1 : 0;
}