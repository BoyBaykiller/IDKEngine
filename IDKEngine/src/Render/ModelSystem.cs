using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Render.Objects;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class ModelSystem : IDisposable
    {
        public GpuDrawElementsCmd[] DrawCommands = Array.Empty<GpuDrawElementsCmd>();
        private readonly TypedBuffer<GpuDrawElementsCmd> drawCommandBuffer;

        public GpuMesh[] Meshes = Array.Empty<GpuMesh>();
        private readonly TypedBuffer<GpuMesh> meshBuffer;

        public GpuMeshInstance[] MeshInstances = Array.Empty<GpuMeshInstance>();
        private readonly TypedBuffer<GpuMeshInstance> meshInstanceBuffer;

        public GpuMaterial[] Materials = Array.Empty<GpuMaterial>();
        private readonly TypedBuffer<GpuMaterial> materialBuffer;

        public GpuVertex[] Vertices = Array.Empty<GpuVertex>();
        private readonly TypedBuffer<GpuVertex> vertexBuffer;

        public Vector3[] VertexPositions = Array.Empty<Vector3>();
        private readonly TypedBuffer<Vector3> vertexPositionBuffer;

        public uint[] VertexIndices = Array.Empty<uint>();
        private readonly TypedBuffer<uint> vertexIndicesBuffer;

        public GpuMeshletTaskCmd[] MeshTasksCmds = Array.Empty<GpuMeshletTaskCmd>();
        private readonly TypedBuffer<GpuMeshletTaskCmd> meshletTasksCmdsBuffer;

        public GpuMeshlet[] Meshlets = Array.Empty<GpuMeshlet>();
        private readonly TypedBuffer<GpuMeshlet> meshletBuffer;

        public GpuMeshletInfo[] MeshletsInfo = Array.Empty<GpuMeshletInfo>();
        private readonly TypedBuffer<GpuMeshletInfo> meshletInfoBuffer;

        public uint[] MeshletsVertexIndices = Array.Empty<uint>();
        private readonly TypedBuffer<uint> meshletsVertexIndicesBuffer;

        public byte[] MeshletsLocalIndices = Array.Empty<byte>();
        private readonly TypedBuffer<byte> meshletsPrimitiveIndicesBuffer;

        public BVH BVH;

        private readonly VAO vao;
        public unsafe ModelSystem()
        {
            drawCommandBuffer = new TypedBuffer<GpuDrawElementsCmd>();
            meshBuffer = new TypedBuffer<GpuMesh>();
            meshInstanceBuffer = new TypedBuffer<GpuMeshInstance>();
            materialBuffer = new TypedBuffer<GpuMaterial>();
            vertexBuffer = new TypedBuffer<GpuVertex>();
            vertexPositionBuffer = new TypedBuffer<Vector3>();
            vertexIndicesBuffer = new TypedBuffer<uint>();
            meshletTasksCmdsBuffer = new TypedBuffer<GpuMeshletTaskCmd>();
            meshletBuffer = new TypedBuffer<GpuMeshlet>();
            meshletInfoBuffer = new TypedBuffer<GpuMeshletInfo>();
            meshletsVertexIndicesBuffer = new TypedBuffer<uint>();
            meshletsPrimitiveIndicesBuffer = new TypedBuffer<byte>();

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
                Helper.ArrayAdd(ref Meshes, models[i].Meshes);
                for (int j = Meshes.Length - models[i].Meshes.Length; j < Meshes.Length; j++)
                {
                    ref GpuMesh mesh = ref Meshes[j];
                    mesh.MaterialIndex += Materials.Length;
                    mesh.MeshletsStart += Meshlets.Length;
                }

                Helper.ArrayAdd(ref Meshlets, models[i].Meshlets);
                for (int j = Meshlets.Length - models[i].Meshlets.Length; j < Meshlets.Length; j++)
                {
                    ref GpuMeshlet meshlet = ref Meshlets[j];
                    meshlet.VertexOffset += (uint)MeshletsVertexIndices.Length;
                    meshlet.IndicesOffset += (uint)MeshletsLocalIndices.Length;
                }

                Helper.ArrayAdd(ref DrawCommands, models[i].DrawCommands);
                int prevLength = DrawCommands.Length - models[i].DrawCommands.Length;
                for (int j = prevLength; j < DrawCommands.Length; j++)
                {
                    ref GpuDrawElementsCmd drawCmd = ref DrawCommands[j];
                    drawCmd.BaseInstance += prevLength;
                    drawCmd.BaseVertex += Vertices.Length;
                    drawCmd.FirstIndex += VertexIndices.Length;
                }


                Helper.ArrayAdd(ref MeshInstances, models[i].MeshInstances);
                Helper.ArrayAdd(ref Materials, models[i].Materials);
                
                Helper.ArrayAdd(ref Vertices, models[i].Vertices);
                Helper.ArrayAdd(ref VertexPositions, models[i].VertexPositions);
                Helper.ArrayAdd(ref VertexIndices, models[i].VertexIndices);
                
                Helper.ArrayAdd(ref MeshTasksCmds, models[i].MeshTasksCmds);
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

            drawCommandBuffer.MutableAllocate(DrawCommands);
            meshBuffer.MutableAllocate(Meshes);
            meshInstanceBuffer.MutableAllocate(MeshInstances);
            materialBuffer.MutableAllocate(Materials);

            vertexBuffer.MutableAllocate(Vertices);
            vertexPositionBuffer.MutableAllocate(VertexPositions);
            vertexIndicesBuffer.MutableAllocate(VertexIndices);

            meshletTasksCmdsBuffer.MutableAllocate(MeshTasksCmds);
            meshletBuffer.MutableAllocate(Meshlets);
            meshletInfoBuffer.MutableAllocate(MeshletsInfo);
            meshletsVertexIndicesBuffer.MutableAllocate(MeshletsVertexIndices);
            meshletsPrimitiveIndicesBuffer.MutableAllocate(MeshletsLocalIndices);
        }

        public unsafe void Draw()
        {
            vao.Bind();
            drawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer);
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, IntPtr.Zero, Meshes.Length, sizeof(GpuDrawElementsCmd));
        }

        /// <summary>
        /// Requires support for GL_NV_mesh_shader
        /// </summary>
        public unsafe void MeshShaderDrawNV()
        {
            meshletTasksCmdsBuffer.Bind(BufferTarget.DrawIndirectBuffer);
            GL.NV.MultiDrawMeshTasksIndirect(IntPtr.Zero, MeshTasksCmds.Length, sizeof(GpuMeshletTaskCmd));
        }

        public unsafe void UpdateMeshBuffer(int start, int count)
        {
            if (count == 0) return;
            meshBuffer.UploadElements(start, count, Meshes[start]);
        }

        public unsafe void UpdateDrawCommandBuffer(int start, int count)
        {
            if (count == 0) return;
            drawCommandBuffer.UploadElements(start, count, DrawCommands[start]);
        }

        public unsafe void UpdateMeshInstanceBuffer(int start, int count)
        {
            if (count == 0) return;
            meshInstanceBuffer.UploadElements(start, count, MeshInstances[start]);
        }

        public unsafe void UpdateVertexPositions(int start, int count)
        {
            if (count == 0) return;
            vertexPositionBuffer.UploadElements(start, count, VertexPositions[start]);
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
