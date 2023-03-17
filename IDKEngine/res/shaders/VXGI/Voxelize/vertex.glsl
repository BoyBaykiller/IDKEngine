#version 460 core
#extension GL_ARB_bindless_texture : require

layout(binding = 0, rgba16f) restrict uniform image3D ImgResult;

struct DrawCommand
{
    uint Count;
    uint InstanceCount;
    uint FirstIndex;
    uint BaseVertex;
    uint BaseInstance;
};

struct Mesh
{
    int InstanceCount;
    int MaterialIndex;
    float NormalMapStrength;
    float EmissiveBias;
    float SpecularBias;
    float RoughnessBias;
    float RefractionChance;
    float IOR;
    vec3 Absorbance;
    uint CubemapShadowCullInfo;
};

struct Vertex
{
    vec3 Position;
    float _pad0;

    vec2 TexCoord;
    uint Tangent;
    uint Normal;
};

struct MeshInstance
{
    mat4 ModelMatrix;
    mat4 InvModelMatrix;
    mat4 PrevModelMatrix;
};

layout(std430, binding = 0) restrict readonly buffer DrawCommandsSSBO
{
    DrawCommand DrawCommands[];
} drawCommandSSBO;

layout(std430, binding = 2) restrict readonly buffer MeshSSBO
{
    Mesh Meshes[];
} meshSSBO;

layout(std430, binding = 4) restrict readonly buffer MatrixSSBO
{
    MeshInstance MeshInstances[];
} meshInstanceSSBO;

layout(std430, binding = 9) restrict readonly buffer VertexSSBO
{
    Vertex Vertices[];
} vertexSSBO;

layout(std430, binding = 10) restrict readonly buffer IndicesSSBO
{
    uint Indices[];
} indicesSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    float _pad0;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaUpdate;
    float Time;
} basicDataUBO;

layout(std140, binding = 5) uniform VoxelizerDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} voxelizerDataUBO;

out InOutVars
{
    vec3 FragPos;
    vec2 TexCoord;
    vec3 Normal;
    uint MaterialIndex;
    float EmissiveBias;
} outData;

vec3 DecompressSNorm32Fast(uint data);

void main()
{
    Mesh mesh = meshSSBO.Meshes[gl_DrawID];
    DrawCommand cmd = drawCommandSSBO.DrawCommands[gl_DrawID];
    MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[gl_InstanceID + gl_BaseInstance];

    Vertex vertex = vertexSSBO.Vertices[indicesSSBO.Indices[gl_VertexID + cmd.FirstIndex] + cmd.BaseVertex];

    outData.FragPos = (meshInstance.ModelMatrix * vec4(vertex.Position, 1.0)).xyz;

    vec3 normal = DecompressSNorm32Fast(vertex.Normal);

    mat3 normalToWorld = mat3(transpose(meshInstance.InvModelMatrix));
    outData.Normal = normalize(normalToWorld * normal);
    outData.TexCoord = vertex.TexCoord;

    outData.MaterialIndex = mesh.MaterialIndex;
    outData.EmissiveBias = mesh.EmissiveBias;

    gl_Position = voxelizerDataUBO.OrthoProjection * vec4(outData.FragPos, 1.0);
}

vec3 DecompressSNorm32Fast(uint data)
{
    float r = (data >> 0) & ((1u << 11) - 1);
    float g = (data >> 11) & ((1u << 11) - 1);
    float b = (data >> 22) & ((1u << 10) - 1);

    r /= (1u << 11) - 1;
    g /= (1u << 11) - 1;
    b /= (1u << 10) - 1;

    return vec3(r, g, b) * 2.0 - 1.0;
}