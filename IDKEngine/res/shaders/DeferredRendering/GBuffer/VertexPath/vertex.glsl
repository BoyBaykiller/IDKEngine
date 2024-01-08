#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in uint Tangent;
layout(location = 3) in uint Normal;

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

layout(std430, binding = 2) restrict readonly buffer MeshInstanceSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

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

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int Samples;
    float MipmapBias;
    int TemporalAntiAliasingMode;
} taaDataUBO;

out InOutVars
{
    vec2 TexCoord;
    vec4 PrevClipPos;
    vec3 Normal;
    vec3 Tangent;
    uint MeshID;
} outData;

void main()
{
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance];
    
    vec3 normal = DecompressSR11G11B10(Normal);
    vec3 tangent = DecompressSR11G11B10(Tangent);

    mat3 unitVecToWorld = mat3(transpose(meshInstance.InvModelMatrix));

    outData.Normal = unitVecToWorld * normal;
    outData.Tangent = unitVecToWorld * tangent;
    outData.TexCoord = TexCoord;
    
    vec4 clipPos = basicDataUBO.ProjView * meshInstance.ModelMatrix * vec4(Position, 1.0);
    outData.PrevClipPos = basicDataUBO.PrevProjView * meshInstance.PrevModelMatrix * vec4(Position, 1.0);
    
    outData.MeshID = gl_DrawID;

    // Add jitter independent of perspective by multiplying with w
    vec4 jitteredClipPos = clipPos;
    jitteredClipPos.xy += taaDataUBO.Jitter * jitteredClipPos.w;

    // if (gl_DrawID == 67)
    // {
    //     uint ass = 0;
    //     for (int i = 0; i < 500000; i++)
    //     {
    //         ass += i;
    //     }
    //     jitteredClipPos.x += ass / 1000000000000.0; 
    // }
    
    gl_Position = jitteredClipPos;
}
