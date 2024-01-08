using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ModelSystem : IDisposable
    {
        public GpuDrawElementsCmd[] DrawCommands = Array.Empty<GpuDrawElementsCmd>();
        private readonly BufferObject drawCommandBuffer;

        public GpuMesh[] Meshes = Array.Empty<GpuMesh>();
        private readonly BufferObject meshBuffer;

        public GpuMeshInstance[] MeshInstances = Array.Empty<GpuMeshInstance>();
        private readonly BufferObject meshInstanceBuffer;

        public GpuMaterial[] Materials = Array.Empty<GpuMaterial>();
        private readonly BufferObject materialBuffer;

        public GpuVertex[] Vertices = Array.Empty<GpuVertex>();
        private readonly BufferObject vertexBuffer;

        public Vector3[] VertexPositions = Array.Empty<Vector3>();
        private readonly BufferObject vertexPositionBuffer;

        public uint[] VertexIndices = Array.Empty<uint>();
        private readonly BufferObject vertexIndicesBuffer;

        public GpuMeshletTaskCmd[] MeshTasksCmds = Array.Empty<GpuMeshletTaskCmd>();
        private readonly BufferObject meshletTasksCmdsBuffer;

        public GpuMeshlet[] Meshlets = Array.Empty<GpuMeshlet>();
        private readonly BufferObject meshletBuffer;

        public GpuMeshletInfo[] MeshletsInfo = Array.Empty<GpuMeshletInfo>();
        private readonly BufferObject meshletInfoBuffer;

        public uint[] MeshletsVertexIndices = Array.Empty<uint>();
        private readonly BufferObject meshletsVertexIndicesBuffer;

        public byte[] MeshletsLocalIndices = Array.Empty<byte>();
        private readonly BufferObject meshletsPrimitiveIndicesBuffer;

        public BVH BVH;

        private readonly VAO vao;
        public unsafe ModelSystem()
        {
            drawCommandBuffer = new BufferObject();
            meshBuffer = new BufferObject();
            meshInstanceBuffer = new BufferObject();
            materialBuffer = new BufferObject();
            vertexBuffer = new BufferObject();
            vertexIndicesBuffer = new BufferObject();
            meshletTasksCmdsBuffer = new BufferObject();
            meshletBuffer = new BufferObject();
            meshletInfoBuffer = new BufferObject();
            vertexPositionBuffer = new BufferObject();
            meshletsVertexIndicesBuffer = new BufferObject();
            meshletsPrimitiveIndicesBuffer = new BufferObject();

            drawCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0);
            meshBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1);
            meshInstanceBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2);
            materialBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3);
            vertexBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4);
            vertexPositionBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 10);
            meshletTasksCmdsBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 11);
            meshletBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 12);
            meshletInfoBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 13);
            meshletsVertexIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 14);
            meshletsPrimitiveIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 15);

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

        public unsafe void Add(params ModelLoader.Model[] models)
        {
            if (models.Length == 0)
            {
                return;
            }

            int prevDrawCommandsLength = DrawCommands.Length;
            
            for (int i = 0; i < models.Length; i++)
            {
                // Upon deletion these are the properties that need to be adjusted
                for (int j = 0; j < models[i].Meshes.Length; j++)
                {
                    ref GpuMesh mesh = ref models[i].Meshes[j];
                    mesh.MaterialIndex += Materials.Length;
                    mesh.MeshletsStart += Meshlets.Length;
                }
                for (int j = 0; j < models[i].Meshlets.Length; j++)
                {
                    ref GpuMeshlet meshlet = ref models[i].Meshlets[j];
                    meshlet.VertexOffset += (uint)MeshletsVertexIndices.Length;
                    meshlet.IndicesOffset += (uint)MeshletsLocalIndices.Length;
                }
                for (int j = 0; j < models[i].DrawCommands.Length; j++)
                {
                    ref GpuDrawElementsCmd drawCmd = ref models[i].DrawCommands[j];
                    drawCmd.BaseInstance += DrawCommands.Length;
                    drawCmd.BaseVertex += Vertices.Length;
                    drawCmd.FirstIndex += VertexIndices.Length;
                }


                Helper.ArrayAdd(ref DrawCommands, models[i].DrawCommands);
                Helper.ArrayAdd(ref Meshes, models[i].Meshes);
                Helper.ArrayAdd(ref MeshInstances, models[i].MeshInstances);
                Helper.ArrayAdd(ref Materials, models[i].Materials);
                
                Helper.ArrayAdd(ref Vertices, models[i].Vertices);
                Helper.ArrayAdd(ref VertexPositions, models[i].VertexPositions);
                Helper.ArrayAdd(ref VertexIndices, models[i].VertexIndices);
                
                Helper.ArrayAdd(ref MeshTasksCmds, models[i].MeshTasksCmds);
                Helper.ArrayAdd(ref Meshlets, models[i].Meshlets);
                Helper.ArrayAdd(ref MeshletsInfo, models[i].MeshletsInfo);
                Helper.ArrayAdd(ref MeshletsVertexIndices, models[i].MeshletsVertexIndices);
                Helper.ArrayAdd(ref MeshletsLocalIndices, models[i].MeshletsLocalIndices);
            }

            // Handle BVH build
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

            meshletTasksCmdsBuffer.MutableAllocate(MeshTasksCmds.Length * sizeof(GpuMeshletTaskCmd), MeshTasksCmds);
            meshletBuffer.MutableAllocate(Meshlets.Length * sizeof(GpuMeshlet), Meshlets);
            meshletInfoBuffer.MutableAllocate(MeshletsInfo.Length * sizeof(GpuMeshletInfo), MeshletsInfo);
            meshletsVertexIndicesBuffer.MutableAllocate(MeshletsVertexIndices.Length * sizeof(uint), MeshletsVertexIndices);
            meshletsPrimitiveIndicesBuffer.MutableAllocate(MeshletsLocalIndices.Length * sizeof(byte), MeshletsLocalIndices);
        }

        public unsafe void Draw()
        {
            vao.Bind();
            drawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer);
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, Meshes.Length, sizeof(GpuDrawElementsCmd));
        }

        public unsafe void MeshDraw()
        {
            meshletTasksCmdsBuffer.Bind(BufferTarget.DrawIndirectBuffer);
            GL.NV.MultiDrawMeshTasksIndirect(IntPtr.Zero, MeshTasksCmds.Length, sizeof(GpuMeshletTaskCmd));
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
            meshInstanceBuffer.Dispose();
            materialBuffer.Dispose();
            vertexBuffer.Dispose();
            vertexIndicesBuffer.Dispose();
            meshletTasksCmdsBuffer.Dispose();
            meshletBuffer.Dispose();
            meshletInfoBuffer.Dispose();
            vertexPositionBuffer.Dispose();
            meshletsVertexIndicesBuffer.Dispose();
            meshletsPrimitiveIndicesBuffer.Dispose();

            vao.Dispose();

            BVH.Dispose();
        }
    }
}
