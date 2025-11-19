#version 460 core

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

layout(location = 0) uniform uint InputVertexOffset;
layout(location = 1) uniform uint OutputVertexOffset;
layout(location = 2) uniform uint JointOffset;
layout(location = 3) uniform uint VertexCount;

void main()
{
    if (gl_GlobalInvocationID.x >= VertexCount)
    {
        return;
    }

    uint inIndex = InputVertexOffset + gl_GlobalInvocationID.x;
    uint outIndex = OutputVertexOffset + gl_GlobalInvocationID.x;

    UnskinnedVertex unskinnedVertex = unskinnedVertexSSBO.Vertices[inIndex];

    uvec4 jointIndices = Unpack(unskinnedVertex.JointIndices);
    vec4 jointWeights = Unpack(unskinnedVertex.JointWeights);
    mat4x3 skinMatrix =
        jointWeights.x * jointMatricesSSBO.Matrices[JointOffset + jointIndices.x] +
        jointWeights.y * jointMatricesSSBO.Matrices[JointOffset + jointIndices.y] +
        jointWeights.z * jointMatricesSSBO.Matrices[JointOffset + jointIndices.z] +
        jointWeights.w * jointMatricesSSBO.Matrices[JointOffset + jointIndices.w];
    
    vec3 position = Unpack(unskinnedVertex.Position);
    vec3 normal = DecompressSR11G11B10(unskinnedVertex.Normal);
    vec3 tangent = DecompressSR11G11B10(unskinnedVertex.Tangent);

    position = (skinMatrix * vec4(position, 1.0)).xyz;
    normal = normalize(mat3(skinMatrix) * normal);
    tangent = normalize(mat3(skinMatrix) * tangent);

    prevVertexPositionSSBO.Positions[outIndex] = vertexPositionsSSBO.Positions[outIndex];

    vertexPositionsSSBO.Positions[outIndex] = Pack(position);
    vertexSSBO.Vertices[outIndex].Normal = CompressSR11G11B10(normal);
    vertexSSBO.Vertices[outIndex].Tangent = CompressSR11G11B10(tangent);
}
