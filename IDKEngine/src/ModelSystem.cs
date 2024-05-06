using System;
using System.Collections;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.OpenGL;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class ModelSystem : IDisposable
    {
        public GpuDrawElementsCmd[] DrawCommands = Array.Empty<GpuDrawElementsCmd>();
        public readonly TypedBuffer<GpuDrawElementsCmd> drawCommandBuffer;

        public GpuMesh[] Meshes = Array.Empty<GpuMesh>();
        private readonly TypedBuffer<GpuMesh> meshBuffer;

        public GpuMeshInstance[] MeshInstances = Array.Empty<GpuMeshInstance>();
        private readonly TypedBuffer<GpuMeshInstance> meshInstanceBuffer;

        public uint[] VisibleMeshInstances = Array.Empty<uint>();
        private readonly TypedBuffer<uint> visibleMeshInstanceBuffer;

        public GpuMaterial[] Materials = Array.Empty<GpuMaterial>();
        private readonly TypedBuffer<GpuMaterial> materialBuffer;

        public GpuVertex[] Vertices = Array.Empty<GpuVertex>();
        private readonly TypedBuffer<GpuVertex> vertexBuffer;

        public Vector3[] VertexPositions = Array.Empty<Vector3>();
        private readonly TypedBuffer<Vector3> vertexPositionBuffer;

        public uint[] VertexIndices = Array.Empty<uint>();
        private readonly TypedBuffer<uint> vertexIndicesBuffer;

        public GpuMeshletTaskCmd[] MeshletTasksCmds = Array.Empty<GpuMeshletTaskCmd>();
        private readonly TypedBuffer<GpuMeshletTaskCmd> meshletTasksCmdsBuffer;

        private readonly TypedBuffer<int> meshletTasksCountBuffer;

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
            visibleMeshInstanceBuffer = new TypedBuffer<uint>();
            materialBuffer = new TypedBuffer<GpuMaterial>();
            vertexBuffer = new TypedBuffer<GpuVertex>();
            vertexPositionBuffer = new TypedBuffer<Vector3>();
            vertexIndicesBuffer = new TypedBuffer<uint>();
            meshletTasksCmdsBuffer = new TypedBuffer<GpuMeshletTaskCmd>();
            meshletTasksCountBuffer = new TypedBuffer<int>();
            meshletBuffer = new TypedBuffer<GpuMeshlet>();
            meshletInfoBuffer = new TypedBuffer<GpuMeshletInfo>();
            meshletsVertexIndicesBuffer = new TypedBuffer<uint>();
            meshletsPrimitiveIndicesBuffer = new TypedBuffer<byte>();

            drawCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0);
            meshBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1);
            meshInstanceBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2);
            visibleMeshInstanceBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3);
            materialBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 9);
            vertexBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 10);
            vertexPositionBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 11);
            meshletTasksCmdsBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 12);
            meshletTasksCountBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 13);
            meshletBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 14);
            meshletInfoBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 15);
            meshletsVertexIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 16);
            meshletsPrimitiveIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 17);

            vao = new VAO();
            vao.SetElementBuffer(vertexIndicesBuffer);
            // We use Vertex Pulling, no need to declare vertex format using VAO API

            BVH = new BVH();
        }

        public void Add(params ModelLoader.Model[] models)
        {
            if (models.Length == 0)
            {
                return;
            }

            int prevDrawCommandsLength = DrawCommands.Length;

            for (int i = 0; i < models.Length; i++)
            {
                // Upon deletion these are the properties that need to be adjusted
                Helper.ArrayAdd(ref DrawCommands, models[i].DrawCommands);
                for (int j = DrawCommands.Length - models[i].DrawCommands.Length; j < DrawCommands.Length; j++)
                {
                    ref GpuDrawElementsCmd newDrawCmd = ref DrawCommands[j];
                    newDrawCmd.BaseInstance += MeshInstances.Length;
                    newDrawCmd.BaseVertex += Vertices.Length;
                    newDrawCmd.FirstIndex += VertexIndices.Length;
                }
                Helper.ArrayAdd(ref MeshInstances, models[i].MeshInstances);
                for (int j = MeshInstances.Length - models[i].MeshInstances.Length; j < MeshInstances.Length; j++)
                {
                    ref GpuMeshInstance newMeshInstance = ref MeshInstances[j];
                    newMeshInstance.MeshIndex += Meshes.Length;
                }
                Helper.ArrayAdd(ref Meshes, models[i].Meshes);
                for (int j = Meshes.Length - models[i].Meshes.Length; j < Meshes.Length; j++)
                {
                    ref GpuMesh newMesh = ref Meshes[j];
                    newMesh.MaterialIndex += Materials.Length;
                    newMesh.MeshletsStart += Meshlets.Length;
                }
                Helper.ArrayAdd(ref Meshlets, models[i].Meshlets);
                for (int j = Meshlets.Length - models[i].Meshlets.Length; j < Meshlets.Length; j++)
                {
                    ref GpuMeshlet newMeshlet = ref Meshlets[j];
                    newMeshlet.VertexOffset += (uint)MeshletsVertexIndices.Length;
                    newMeshlet.IndicesOffset += (uint)MeshletsLocalIndices.Length;
                }

                Helper.ArrayAdd(ref Materials, models[i].Materials);

                Helper.ArrayAdd(ref Vertices, models[i].Vertices);
                Helper.ArrayAdd(ref VertexPositions, models[i].VertexPositions);
                Helper.ArrayAdd(ref VertexIndices, models[i].VertexIndices);

                Helper.ArrayAdd(ref MeshletTasksCmds, models[i].MeshletTasksCmds);
                Helper.ArrayAdd(ref MeshletsInfo, models[i].MeshletsInfo);
                Helper.ArrayAdd(ref MeshletsVertexIndices, models[i].MeshletsVertexIndices);
                Helper.ArrayAdd(ref MeshletsLocalIndices, models[i].MeshletsLocalIndices);
            }

            {
                ReadOnlyMemory<GpuDrawElementsCmd> newDrawCommands = new ReadOnlyMemory<GpuDrawElementsCmd>(DrawCommands, prevDrawCommandsLength, DrawCommands.Length - prevDrawCommandsLength);
                BVH.AddMeshes(newDrawCommands, DrawCommands, MeshInstances, VertexPositions, VertexIndices);

                // Adjust root node index in context of all Nodes
                uint bvhNodesExclusiveSum = 0;
                for (int i = 0; i < DrawCommands.Length; i++)
                {
                    Meshes[i].BlasRootNodeIndex = bvhNodesExclusiveSum;
                    bvhNodesExclusiveSum += (uint)BVH.Tlas.Blases[i].Nodes.Length;
                }

                AbstractShaderProgram.ShaderInsertions["MAX_BLAS_TREE_DEPTH"] = BVH.MaxBlasTreeDepth.ToString();
            }

            UploadAllModelData();
            meshInstanceShouldUpload = new BitArray(MeshInstances.Length, true);
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
            meshletTasksCountBuffer.Bind(BufferTarget.ParameterBuffer);
            int maxMeshlets = meshletTasksCmdsBuffer.GetNumElements();
            GL.NV.MultiDrawMeshTasksIndirectCount(IntPtr.Zero, IntPtr.Zero, maxMeshlets, sizeof(GpuMeshletTaskCmd));
        }

        private static BitArray meshInstanceShouldUpload;
        public void Update(out bool anyMeshInstanceMoved)
        {
            anyMeshInstanceMoved = false;

            int batchedUploadSize = 1 << 8;
            int lastCleanBatch = MeshInstances.Length / batchedUploadSize * batchedUploadSize;
            bool shouldBatchUpload = false;
            for (int i = 0; i < MeshInstances.Length;)
            {
                if (MeshInstances[i].DidMove())
                {
                    shouldBatchUpload = true;
                }
                if (meshInstanceShouldUpload[i])
                {
                    meshInstanceShouldUpload[i] = false;
                    shouldBatchUpload = true;
                }

                i++;
                if ((i % batchedUploadSize == 0 || i == MeshInstances.Length) && shouldBatchUpload)
                {
                    int batchSize = batchedUploadSize;
                    int start = i - batchSize;
                    if (i > lastCleanBatch)
                    {
                        batchSize = MeshInstances.Length - lastCleanBatch;
                        start = lastCleanBatch;
                    }

                    UpdateMeshInstanceBuffer(start, batchSize);

                    for (int j = start; j < start + batchSize; j++)
                    {
                        if (MeshInstances[j].DidMove())
                        {
                            MeshInstances[j].SetPrevToCurrentMatrix();

                            // Prev matrix got updated, needs upload next frame
                            meshInstanceShouldUpload[j] = true;
                        }
                    }

                    anyMeshInstanceMoved = true;
                    shouldBatchUpload = false;
                }
            }
        }

        public void UpdateMeshBuffer(int start, int count)
        {
            meshBuffer.UploadElements(start, count, Meshes[start]);
        }

        public void UpdateDrawCommandBuffer(int start, int count)
        {
            drawCommandBuffer.UploadElements(start, count, DrawCommands[start]);
        }

        public void UpdateMeshInstanceBuffer(int start, int count)
        {
            meshInstanceBuffer.UploadElements(start, count, MeshInstances[start]);
        }

        public void UpdateVertexPositions(int start, int count)
        {
            vertexPositionBuffer.UploadElements(start, count, VertexPositions[start]);
        }

        public void ResetInstancesBeforeCulling(int count = 0)
        {
            // for vertex rendering path
            for (int i = 0; i < DrawCommands.Length; i++)
            {
                DrawCommands[i].InstanceCount = count;
            }
            UpdateDrawCommandBuffer(0, DrawCommands.Length);
            for (int i = 0; i < DrawCommands.Length; i++)
            {
                ref readonly GpuMesh mesh = ref Meshes[i];
                DrawCommands[i].InstanceCount = mesh.InstanceCount;
            }

            // for mesh-shader rendering path
            meshletTasksCountBuffer.UploadElements(count);
        }

        public void UploadAllModelData()
        {
            drawCommandBuffer.MutableAllocateElements(DrawCommands);
            meshBuffer.MutableAllocateElements(Meshes);
            meshInstanceBuffer.MutableAllocateElements(MeshInstances);
            visibleMeshInstanceBuffer.MutableAllocateElements(MeshInstances.Length * 6); // * 6 for PointShadow cubemap culling
            materialBuffer.MutableAllocateElements(Materials);

            vertexBuffer.MutableAllocateElements(Vertices);
            vertexPositionBuffer.MutableAllocateElements(VertexPositions);
            vertexIndicesBuffer.MutableAllocateElements(VertexIndices);

            meshletTasksCmdsBuffer.MutableAllocateElements(MeshletTasksCmds.Length * 6); // * 6 for PointShadow cubemap culling
            meshletTasksCmdsBuffer.UploadElements(MeshletTasksCmds);
            meshletTasksCountBuffer.MutableAllocateElements(1);
            meshletBuffer.MutableAllocateElements(Meshlets);
            meshletInfoBuffer.MutableAllocateElements(MeshletsInfo);
            meshletsVertexIndicesBuffer.MutableAllocateElements(MeshletsVertexIndices);
            meshletsPrimitiveIndicesBuffer.MutableAllocateElements(MeshletsLocalIndices);
        }

        public int GetMeshVertexCount(int meshID)
        {
            int baseVertex = DrawCommands[meshID].BaseVertex;
            int nextBaseVertex = meshID + 1 == DrawCommands.Length ? VertexPositions.Length : DrawCommands[meshID + 1].BaseVertex;
            return nextBaseVertex - baseVertex;
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
            visibleMeshInstanceBuffer.Dispose();
            materialBuffer.Dispose();
            vertexBuffer.Dispose();
            vertexIndicesBuffer.Dispose();
            meshletTasksCmdsBuffer.Dispose();
            meshletTasksCountBuffer.Dispose();
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
