#version 460 core

#define DECLARE_SKINNING_STORAGE_BUFFERS

AppInclude(include/Constants.glsl)
AppInclude(include/Compression.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

void main()
{
    uint inIndex = gl_VertexID;
    uint outIndex = gl_BaseInstance + gl_VertexID;

    UnskinnedVertex unskinnedVertex = unskinnedVertexSSBO.Vertices[inIndex];

    uvec4 jointIndices = jointIndicesSSBO.Indices[inIndex];
    vec4 jointWeights = jointWeightsSSBO.Weights[inIndex];
    mat4x3 skinMatrix =
        jointWeights.x * jointMatricesSSBO.Matrices[jointIndices.x] +
        jointWeights.y * jointMatricesSSBO.Matrices[jointIndices.y] +
        jointWeights.z * jointMatricesSSBO.Matrices[jointIndices.z] +
        jointWeights.w * jointMatricesSSBO.Matrices[jointIndices.w];
    
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

    // Outputting NaN to cull vertices is explicitly recommended by vendors https://gpuopen.com/learn/rdna-performance-guide/#shaders
    // and I do see it improving performance here.
    gl_Position = vec4(FLOAT_NAN);
}
