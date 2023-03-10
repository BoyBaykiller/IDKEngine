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

vec3[3] Voxelize(vec3 v0, vec3 v1, vec3 v2);
vec3 DecompressSNorm32Fast(uint data);

// Inserted by application. 0 if false, else 1
#define TAKE_FAST_GEOMETRY_SHADER_PATH __TAKE_FAST_GEOMETRY_SHADER_PATH__

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

#if TAKE_FAST_GEOMETRY_SHADER_PATH
    gl_Position = voxelizerDataUBO.OrthoProjection * vec4(outData.FragPos, 1.0);
#else
    uint triLocalVertexID = gl_VertexID % 3;
    uint triID = gl_VertexID / 3;
    vec3 triNdc[3];

    vec3 p1 = vertexSSBO.Vertices[indicesSSBO.Indices[triID * 3 + cmd.FirstIndex + ((triLocalVertexID + 1) % 3)] + cmd.BaseVertex].Position;
    vec3 p2 = vertexSSBO.Vertices[indicesSSBO.Indices[triID * 3 + cmd.FirstIndex + ((triLocalVertexID + 2) % 3)] + cmd.BaseVertex].Position;

    triNdc[((triLocalVertexID + 0) % 3)] = outData.FragPos;
    triNdc[((triLocalVertexID + 1) % 3)] = (meshInstance.ModelMatrix * vec4(p1, 1.0)).xyz;
    triNdc[((triLocalVertexID + 2) % 3)] = (meshInstance.ModelMatrix * vec4(p2, 1.0)).xyz;
    
    triNdc[0] = (voxelizerDataUBO.OrthoProjection * vec4(triNdc[0], 1.0)).xyz;
    triNdc[1] = (voxelizerDataUBO.OrthoProjection * vec4(triNdc[1], 1.0)).xyz;
    triNdc[2] = (voxelizerDataUBO.OrthoProjection * vec4(triNdc[2], 1.0)).xyz;
    triNdc = Voxelize(triNdc[0], triNdc[1], triNdc[2]);
    
    gl_Position = vec4(triNdc[triLocalVertexID], 1.0);
#endif
}

vec3[3] Voxelize(vec3 v0, vec3 v1, vec3 v2)
{
    vec3 p1 = v1 - v0;
    vec3 p2 = v2 - v0;
    vec3 normalWeights = abs(cross(p1, p2));

    int dominantAxis = normalWeights.y > normalWeights.x ? 1 : 0;
    dominantAxis = normalWeights.z > normalWeights[dominantAxis] ? 2 : dominantAxis;

    vec3 outNdc[3] = { v0, v1, v2 };
    for (int i = 0; i < 3; i++)
    {
        vec3 ndc = outNdc[i];

        // Select the projection plane that yields the biggest projection area 
        if (dominantAxis == 0) ndc = ndc.zyx;
        else if (dominantAxis == 1) ndc = ndc.xzy;

        outNdc[i] = ndc;
    }

    // Dilate Triangle
    // Source: https://wickedengine.net/2017/08/30/voxel-based-global-illumination/
    vec2 viewportPixelSize = 1.0 / imageSize(ImgResult).xy;
    vec2 side0N = normalize(outNdc[1].xy - outNdc[0].xy);
    vec2 side1N = normalize(outNdc[2].xy - outNdc[1].xy);
    vec2 side2N = normalize(outNdc[0].xy - outNdc[2].xy);

    outNdc[0].xy += normalize(side2N - side0N) * viewportPixelSize;
    outNdc[1].xy += normalize(side0N - side1N) * viewportPixelSize;
    outNdc[2].xy += normalize(side1N - side2N) * viewportPixelSize;

    return outNdc;
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