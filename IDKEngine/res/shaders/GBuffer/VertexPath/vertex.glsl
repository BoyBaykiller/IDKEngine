#version 460 core

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/Transformations.glsl)

layout(location = 0) in vec3 Position;
layout(location = 1) in vec2 TexCoord;
layout(location = 2) in uint Tangent;
layout(location = 3) in uint Normal;

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
    uint meshInstanceID = visibleMeshInstanceSSBO.MeshInstanceIDs[gl_InstanceID + gl_BaseInstance];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceID];
    
    vec3 normal = DecompressSR11G11B10(Normal);
    vec3 tangent = DecompressSR11G11B10(Tangent);
    mat4 modelMatrix = mat4(meshInstance.ModelMatrix);
    mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
    mat4 prevModelMatrix = mat4(meshInstance.PrevModelMatrix);
    mat3 unitVecToWorld = mat3(transpose(invModelMatrix));

    outData.Normal = normalize(unitVecToWorld * normal);
    outData.Tangent = normalize(unitVecToWorld * tangent);
    outData.TexCoord = TexCoord;
    outData.PrevClipPos = perFrameDataUBO.PrevProjView * prevModelMatrix * vec4(Position, 1.0);
    outData.MeshID = gl_DrawID;
    
    vec4 clipPos = perFrameDataUBO.ProjView * modelMatrix * vec4(Position, 1.0);

    // Add jitter independent of perspective by multiplying with w
    vec4 jitteredClipPos = clipPos;
    jitteredClipPos.xy += taaDataUBO.Jitter * jitteredClipPos.w;
    
    gl_Position = jitteredClipPos;
}
