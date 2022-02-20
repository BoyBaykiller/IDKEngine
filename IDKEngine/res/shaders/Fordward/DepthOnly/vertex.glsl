#version 460 core

layout(location = 0) in vec3 Position;

struct Mesh
{
    mat4 Model;
    mat4 PrevModel;
    int MaterialIndex;
    int BVHEntry;
    int _pad0;
    int _pad1;
};

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

layout(std140, binding = 4) uniform TAASettingsUBO
{
    vec4 HaltonSequence[256];
} taaSettingsUBO;

void main()
{
    mat4 model = meshSSBO.Meshes[gl_DrawID].Model;
    vec3 fragPos = (model * vec4(Position, 1.0)).xyz;
    
    vec4 clipPos = basicDataUBO.ProjView * vec4(fragPos, 1.0);

    int rawIndex = basicDataUBO.FrameCount % taaSettingsUBO.HaltonSequence.length();
    int mapedIndex = rawIndex / 2; 
    int componentIndex = rawIndex % 2;
    vec2 jitter = vec2(taaSettingsUBO.HaltonSequence[mapedIndex][componentIndex + 0], taaSettingsUBO.HaltonSequence[mapedIndex][componentIndex + 1]);
    gl_Position = vec4(clipPos.xy + clipPos.w * jitter, clipPos.zw);
}