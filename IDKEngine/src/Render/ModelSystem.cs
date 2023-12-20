using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Render.Objects;
using System.Runtime.InteropServices;

namespace IDKEngine.Render
{
    class ModelSystem : IDisposable
    {
        public int TriangleCount => VertexIndices.Length / 3;


        public GpuDrawElementsCmd[] DrawCommands;
        private readonly BufferObject drawCommandBuffer;

        public GpuMesh[] Meshes;
        private readonly BufferObject meshBuffer;

        public GpuMeshInstance[] MeshInstances;
        private readonly BufferObject meshInstanceBuffer;

        public GpuMaterial[] Materials;
        private readonly BufferObject materialBuffer;

        public GpuVertex[] Vertices;
        private readonly BufferObject vertexBuffer;

        public Vector3[] VertexPositions;
        private readonly BufferObject vertexPositionBuffer;

        public uint[] VertexIndices;
        private readonly BufferObject vertexIndicesBuffer;

        public BVH BVH;

        private readonly VAO vao;
        public unsafe ModelSystem()
        {
            DrawCommands = Array.Empty<GpuDrawElementsCmd>();
            drawCommandBuffer = new BufferObject();
            drawCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0);

            Meshes = Array.Empty<GpuMesh>();
            meshBuffer = new BufferObject();
            meshBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1);

            MeshInstances = Array.Empty<GpuMeshInstance>();
            meshInstanceBuffer = new BufferObject();
            meshInstanceBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2);

            Materials = Array.Empty<GpuMaterial>();
            materialBuffer = new BufferObject();
            materialBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3);

            Vertices = Array.Empty<GpuVertex>();
            vertexBuffer = new BufferObject();
            vertexBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4);

            VertexPositions = Array.Empty<Vector3>();
            vertexPositionBuffer = new BufferObject();

            VertexIndices = Array.Empty<uint>();
            vertexIndicesBuffer = new BufferObject();
            vertexIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 12);

            vao = new VAO();
            vao.SetElementBuffer(vertexIndicesBuffer);

            vao.AddSourceBuffer(vertexPositionBuffer, 0, sizeof(Vector3));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, 0);

            vao.AddSourceBuffer(vertexBuffer, 1, sizeof(GpuVertex));
            vao.SetAttribFormat(1, 1, 2, VertexAttribType.Float, (int)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.TexCoord)));
            vao.SetAttribFormatI(1, 2, 1, VertexAttribType.UnsignedInt, (int)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.Tangent)));
            vao.SetAttribFormatI(1, 3, 1, VertexAttribType.UnsignedInt, (int)Marshal.OffsetOf<GpuVertex>(nameof(GpuVertex.Normal)));

            BVH = new BVH();
        }

        public unsafe void Add(params Model[] models)
        {
            if (models.Length == 0)
            {
                return;
            }

            int prevDrawCommandsLength = DrawCommands.Length;
            for (int i = 0; i < models.Length; i++)
            {
                // Don't modify order
                LoadDrawCommands(models[i].DrawCommands);
                LoadVertices(models[i].Vertices);
                LoadVertexPositions(models[i].VertexPositions);

                // Don't modify order
                LoadMeshes(models[i].Meshes);
                LoadMaterials(models[i].Materials);

                LoadIndices(models[i].Indices);
                LoadMeshInstances(models[i].MeshInstances);
            }

            {
                ReadOnlyMemory<GpuDrawElementsCmd> newDrawCommands = new ReadOnlyMemory<GpuDrawElementsCmd>(DrawCommands, prevDrawCommandsLength, DrawCommands.Length - prevDrawCommandsLength);
                BVH.AddMeshesAndBuild(newDrawCommands, DrawCommands, MeshInstances, VertexPositions, VertexIndices);

                // Caculate root node BVH index for each mesh
                uint bvhNodesExclusiveSum = 0;
                for (int i = 0; i < DrawCommands.Length; i++)
                {
                    DrawCommands[i].BlasRootNodeIndex = bvhNodesExclusiveSum;
                    bvhNodesExclusiveSum += (uint)BVH.Tlas.Blases[i].Nodes.Length;
                }
            }

            drawCommandBuffer.MutableAllocate(DrawCommands.Length * sizeof(GpuDrawElementsCmd), DrawCommands);
            meshBuffer.MutableAllocate(Meshes.Length * sizeof(GpuMesh), Meshes);
            meshInstanceBuffer.MutableAllocate(MeshInstances.Length * sizeof(GpuMeshInstance), MeshInstances);
            materialBuffer.MutableAllocate(Materials.Length * sizeof(GpuMaterial), Materials);
            vertexBuffer.MutableAllocate(Vertices.Length * sizeof(GpuVertex), Vertices);
            vertexPositionBuffer.MutableAllocate(VertexPositions.Length * sizeof(Vector3), VertexPositions);
            vertexIndicesBuffer.MutableAllocate(VertexIndices.Length * sizeof(uint), VertexIndices);
        }

        public unsafe void Draw()
        {
            if (Meshes.Length == 0)
            {
                return;
            }

            vao.Bind();
            drawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer);
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, 0, Meshes.Length, sizeof(GpuDrawElementsCmd));
        }

        public unsafe void UpdateMeshBuffer(int start, int count)
        {
            if (count == 0) return;
            meshBuffer.SubData(start * sizeof(GpuMesh), count * sizeof(GpuMesh), Meshes[start]);
        }

        public unsafe void UpdateDrawCommandBuffer(int start, int count)
        {
            if (count == 0) return;
            drawCommandBuffer.SubData(start * sizeof(GpuDrawElementsCmd), count * sizeof(GpuDrawElementsCmd), DrawCommands[start]);
        }

        public unsafe void UpdateMeshInstanceBuffer(int start, int count)
        {
            if (count == 0) return;
            meshInstanceBuffer.SubData(start * sizeof(GpuMeshInstance), count * sizeof(GpuMeshInstance), MeshInstances[start]);
        }

        private void LoadDrawCommands(ReadOnlySpan<GpuDrawElementsCmd> drawCommands)
        {
            int prevCmdLength = DrawCommands.Length;
            int prevIndicesLength = DrawCommands.Length == 0 ? 0 : DrawCommands[prevCmdLength - 1].FirstIndex + DrawCommands[prevCmdLength - 1].Count;
            int prevBaseVertex = DrawCommands.Length == 0 ? 0 : DrawCommands[prevCmdLength - 1].BaseVertex + GetMeshVertexCount(prevCmdLength - 1);
            Helper.ArrayAdd(ref DrawCommands, drawCommands);

            for (int i = 0; i < drawCommands.Length; i++)
            {
                DrawCommands[prevCmdLength + i].BaseInstance += prevCmdLength;
                DrawCommands[prevCmdLength + i].BaseVertex += prevBaseVertex;
                DrawCommands[prevCmdLength + i].FirstIndex += prevIndicesLength;
            }
        }
        private void LoadMeshes(ReadOnlySpan<GpuMesh> meshes)
        {
            int prevMeshesLength = Meshes.Length;
            int prevMaterialsLength = Materials.Length;
            Helper.ArrayAdd(ref Meshes, meshes);

            for (int i = 0; i < meshes.Length; i++)
            {
                Meshes[prevMeshesLength + i].MaterialIndex += prevMaterialsLength;
            }

        }
        private void LoadMeshInstances(ReadOnlySpan<GpuMeshInstance> meshInstances)
        {
            Helper.ArrayAdd(ref MeshInstances, meshInstances);
        }
        private void LoadMaterials(ReadOnlySpan<GpuMaterial> materials)
        {
            Helper.ArrayAdd(ref Materials, materials);
        }
        private void LoadIndices(ReadOnlySpan<uint> indices)
        {
            Helper.ArrayAdd(ref VertexIndices, indices);
        }
        private void LoadVertices(ReadOnlySpan<GpuVertex> vertices)
        {
            Helper.ArrayAdd(ref Vertices, vertices);
        }
        private void LoadVertexPositions(ReadOnlySpan<Vector3> positions)
        {
            Helper.ArrayAdd(ref VertexPositions, positions);
        }

        public int GetMeshVertexCount(int meshIndex)
        {
            return ((meshIndex + 1 > DrawCommands.Length - 1) ? Vertices.Length : DrawCommands[meshIndex + 1].BaseVertex) - DrawCommands[meshIndex].BaseVertex;
        }

        public GpuTriangle GetTriangle(int indicesIndex, int baseVertex)
        {
            GpuTriangle triangle;
            triangle.Vertex0 = Vertices[VertexIndices[indicesIndex + 0] + baseVertex];
            triangle.Vertex1 = Vertices[VertexIndices[indicesIndex + 1] + baseVertex];
            triangle.Vertex2 = Vertices[VertexIndices[indicesIndex + 2] + baseVertex];
            return triangle;
        }

        public void Dispose()
        {
            drawCommandBuffer.Dispose();
            meshBuffer.Dispose();
            materialBuffer.Dispose();
            vertexBuffer.Dispose();
            vertexIndicesBuffer.Dispose();
            meshInstanceBuffer.Dispose();

            vao.Dispose();
        }
    }
}
