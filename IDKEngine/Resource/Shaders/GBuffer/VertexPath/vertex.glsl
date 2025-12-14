#version 460 core

AppInclude(include/Compression.glsl)
AppInclude(include/Math.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

out InOutData
{
    vec2 TexCoord;
    vec4 PrevClipPos;
    vec3 Normal;
    vec3 Tangent;
    uint MeshId;
} outData;

void main()
{
    GpuVertex vertex = vertexSSBO.Vertices[gl_VertexID];
    vec3 vertexPosition = Unpack(vertexPositionsSSBO.Positions[gl_VertexID]);
    vec3 prevVertexPosition = Unpack(prevVertexPositionSSBO.Positions[gl_VertexID]);
    
    uint meshInstanceId = visibleMeshInstanceIdSSBO.Ids[gl_BaseInstance + gl_InstanceID];
    GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[meshInstanceId];
    GpuMeshTransform meshTransform = meshTransformSSBO.Transforms[meshInstance.MeshTransformId];

    vec3 normal = DecompressSR11G11B10(vertex.Normal);
    vec3 tangent = DecompressSR11G11B10(vertex.Tangent);
    mat4 modelMatrix = mat4(meshTransform.ModelMatrix);
    mat4 invModelMatrix = mat4(meshTransform.InvModelMatrix);
    mat4 prevModelMatrix = mat4(meshTransform.PrevModelMatrix);
    mat3 unitVecToWorld = mat3(transpose(invModelMatrix));

    outData.Normal = normalize(unitVecToWorld * normal);
    outData.Tangent = normalize(unitVecToWorld * tangent);
    outData.TexCoord = Unpack(vertex.TexCoord);
    outData.MeshId = gl_DrawID;
    outData.PrevClipPos = perFrameDataUBO.PrevProjView * prevModelMatrix * vec4(prevVertexPosition, 1.0);
    
    vec4 clipPos = perFrameDataUBO.ProjView * modelMatrix * vec4(vertexPosition, 1.0);

    // Add jitter independent of perspective by multiplying with w
    vec4 jitteredClipPos = clipPos;
    jitteredClipPos.xy += taaDataUBO.Jitter * jitteredClipPos.w;

    gl_Position = jitteredClipPos;
}
