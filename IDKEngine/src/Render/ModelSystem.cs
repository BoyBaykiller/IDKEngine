using System;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ModelSystem : IDisposable
    {
        public int TriangleCount => Indices.Length / 3;


        public GpuDrawElementsCmd[] DrawCommands;
        private readonly BufferObject drawCommandBuffer;

        public GpuMesh[] Meshes;
        private readonly BufferObject meshBuffer;

        public GpuMeshInstance[] MeshInstances;
        private readonly BufferObject meshInstanceBuffer;

        public GpuMaterial[] Materials;
        private readonly BufferObject materialBuffer;

        public GpuDrawVertex[] Vertices;
        private readonly BufferObject vertexBuffer;

        public uint[] Indices;
        private readonly BufferObject elementBuffer;

        public BVH BVH;

        private readonly VAO vao;
        private readonly ShaderProgram frustumCullingProgram;
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

            Vertices = Array.Empty<GpuDrawVertex>();
            vertexBuffer = new BufferObject();

            Indices = Array.Empty<uint>();
            elementBuffer = new BufferObject();

            vao = new VAO();
            vao.SetElementBuffer(elementBuffer);
            vao.AddSourceBuffer(vertexBuffer, 0, sizeof(GpuDrawVertex));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, sizeof(float) * 0); // Position
            vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, sizeof(float) * 4); // TexCoord
            vao.SetAttribFormatI(0, 2, 1, VertexAttribType.UnsignedInt, sizeof(float) * 6); // Tangent
            vao.SetAttribFormatI(0, 3, 1, VertexAttribType.UnsignedInt, sizeof(float) * 7); // Normal

            frustumCullingProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/SingleView/Frustum/compute.glsl")));

            BVH = new BVH();
        }

        public unsafe void Add(params Model[] models)
        {
            if (models.Length == 0)
            {
                return;
            }

            for (int i = 0; i < models.Length; i++)
            {
                // Don't modify order
                LoadDrawCommands(models[i].DrawCommands);
                LoadVertices(models[i].Vertices);

                // Don't modify order
                LoadMeshes(models[i].Meshes);
                LoadMaterials(models[i].Materials);

                LoadIndices(models[i].Indices);
                LoadModelMatrices(models[i].MeshInstances);
            }

            {
                int addedDrawCommands = models.Sum(model => model.DrawCommands.Length);
                int prevDrawCommandsLength = DrawCommands.Length - addedDrawCommands;
                ReadOnlyMemory<GpuDrawElementsCmd> newDrawCommands = new ReadOnlyMemory<GpuDrawElementsCmd>(DrawCommands, prevDrawCommandsLength, addedDrawCommands);
                BVH.AddMeshesAndBuild(newDrawCommands, DrawCommands, MeshInstances, Vertices, Indices);

                // Caculate root node offset in blas buffer for each mesh
                uint bvhNodesExclusiveSum = 0;
                for (int i = 0; i < DrawCommands.Length; i++)
                {
                    DrawCommands[i].BlasRootNodeIndex = bvhNodesExclusiveSum;
                    bvhNodesExclusiveSum += (uint)BVH.Tlas.Blases[i].Nodes.Length;
                }
            }

            drawCommandBuffer.MutableAllocate(DrawCommands.Length * sizeof(GpuDrawElementsCmd), DrawCommands);
            meshBuffer.MutableAllocate(Meshes.Length * sizeof(GpuMesh), Meshes);
            materialBuffer.MutableAllocate(Materials.Length * sizeof(GpuMaterial), Materials);
            vertexBuffer.MutableAllocate(Vertices.Length * sizeof(GpuDrawVertex), Vertices);
            elementBuffer.MutableAllocate(Indices.Length * sizeof(uint), Indices);
            meshInstanceBuffer.MutableAllocate(MeshInstances.Length * sizeof(GpuMeshInstance), MeshInstances);
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

        public unsafe void FrustumCull(in Matrix4 projView)
        {
            if (Meshes.Length == 0)
            {
                return;
            }

            frustumCullingProgram.Use();
            frustumCullingProgram.Upload(0, projView);

            GL.DispatchCompute((Meshes.Length + 64 - 1) / 64, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
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

            Array.Resize(ref DrawCommands, prevCmdLength + drawCommands.Length);
            drawCommands.CopyTo(new Span<GpuDrawElementsCmd>(DrawCommands, prevCmdLength, drawCommands.Length));

            for (int i = 0; i < drawCommands.Length; i++)
            {
                // TODO: Fix calculation of base instance to account for more than 1 instance per gltfModel
                DrawCommands[prevCmdLength + i].BaseInstance += prevCmdLength;
                DrawCommands[prevCmdLength + i].BaseVertex += prevBaseVertex;
                DrawCommands[prevCmdLength + i].FirstIndex += prevIndicesLength;
            }
        }
        private void LoadMeshes(ReadOnlySpan<GpuMesh> meshes)
        {
            int prevMeshesLength = Meshes.Length;
            int prevMaterialsLength = Materials.Length;
            Array.Resize(ref Meshes, prevMeshesLength + meshes.Length);
            meshes.CopyTo(new Span<GpuMesh>(Meshes, prevMeshesLength, meshes.Length));

            for (int i = 0; i < meshes.Length; i++)
            {
                Meshes[prevMeshesLength + i].MaterialIndex += prevMaterialsLength;
            }
        }
        private void LoadModelMatrices(ReadOnlySpan<GpuMeshInstance> meshInstances)
        {
            int prevMatricesLength = MeshInstances.Length;
            Array.Resize(ref MeshInstances, prevMatricesLength + meshInstances.Length);
            meshInstances.CopyTo(new Span<GpuMeshInstance>(MeshInstances, prevMatricesLength, meshInstances.Length));
        }
        private void LoadMaterials(ReadOnlySpan<GpuMaterial> materials)
        {
            int prevMaterialsLength = Materials.Length;
            Array.Resize(ref Materials, prevMaterialsLength + materials.Length);
            materials.CopyTo(new Span<GpuMaterial>(Materials, prevMaterialsLength, materials.Length));
        }
        private void LoadIndices(ReadOnlySpan<uint> indices)
        {
            int prevIndicesLength = Indices.Length;
            Array.Resize(ref Indices, prevIndicesLength + indices.Length);
            indices.CopyTo(new Span<uint>(Indices, prevIndicesLength, indices.Length));
        }
        private void LoadVertices(ReadOnlySpan<GpuDrawVertex> vertices)
        {
            int prevVerticesLength = Vertices.Length;
            Array.Resize(ref Vertices, prevVerticesLength + vertices.Length);
            vertices.CopyTo(new Span<GpuDrawVertex>(Vertices, prevVerticesLength, vertices.Length));
        }

        public int GetMeshVertexCount(int meshIndex)
        {
            return ((meshIndex + 1 > DrawCommands.Length - 1) ? Vertices.Length : DrawCommands[meshIndex + 1].BaseVertex) - DrawCommands[meshIndex].BaseVertex;
        }

        public GpuTriangle GetTriangle(int indicesIndex, int baseVertex)
        {
            GpuTriangle triangle;
            triangle.Vertex0 = Vertices[Indices[indicesIndex + 0] + baseVertex];
            triangle.Vertex1 = Vertices[Indices[indicesIndex + 1] + baseVertex];
            triangle.Vertex2 = Vertices[Indices[indicesIndex + 2] + baseVertex];
            return triangle;
        }

        public void Dispose()
        {
            drawCommandBuffer.Dispose();
            meshBuffer.Dispose();
            materialBuffer.Dispose();
            vertexBuffer.Dispose();
            elementBuffer.Dispose();
            meshInstanceBuffer.Dispose();

            vao.Dispose();

            frustumCullingProgram.Dispose();
        }
    }
}
