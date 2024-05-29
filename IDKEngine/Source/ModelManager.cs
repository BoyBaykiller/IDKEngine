using System;
using System.Collections;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class ModelManager : IDisposable
    {
        public BBG.DrawElementsIndirectCommand[] DrawCommands = Array.Empty<BBG.DrawElementsIndirectCommand>();
        public readonly BBG.TypedBuffer<BBG.DrawElementsIndirectCommand> drawCommandBuffer;

        public GpuMesh[] Meshes = Array.Empty<GpuMesh>();
        private readonly BBG.TypedBuffer<GpuMesh> meshBuffer;

        public GpuMeshInstance[] MeshInstances = Array.Empty<GpuMeshInstance>();
        private readonly BBG.TypedBuffer<GpuMeshInstance> meshInstanceBuffer;

        public uint[] VisibleMeshInstances = Array.Empty<uint>();
        private readonly BBG.TypedBuffer<uint> visibleMeshInstanceBuffer;

        public GpuMaterial[] Materials = Array.Empty<GpuMaterial>();
        private readonly BBG.TypedBuffer<GpuMaterial> materialBuffer;

        public GpuVertex[] Vertices = Array.Empty<GpuVertex>();
        private readonly BBG.TypedBuffer<GpuVertex> vertexBuffer;

        public Vector3[] VertexPositions = Array.Empty<Vector3>();
        private readonly BBG.TypedBuffer<Vector3> vertexPositionBuffer;

        public uint[] VertexIndices = Array.Empty<uint>();
        private readonly BBG.TypedBuffer<uint> vertexIndicesBuffer;

        public BBG.DrawMeshTasksIndirectCommandNV[] MeshletTasksCmds = Array.Empty<BBG.DrawMeshTasksIndirectCommandNV>();
        private readonly BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV> meshletTasksCmdsBuffer;

        private readonly BBG.TypedBuffer<int> meshletTasksCountBuffer;

        public GpuMeshlet[] Meshlets = Array.Empty<GpuMeshlet>();
        private readonly BBG.TypedBuffer<GpuMeshlet> meshletBuffer;

        public GpuMeshletInfo[] MeshletsInfo = Array.Empty<GpuMeshletInfo>();
        private readonly BBG.TypedBuffer<GpuMeshletInfo> meshletInfoBuffer;

        public uint[] MeshletsVertexIndices = Array.Empty<uint>();
        private readonly BBG.TypedBuffer<uint> meshletsVertexIndicesBuffer;

        public byte[] MeshletsLocalIndices = Array.Empty<byte>();
        private readonly BBG.TypedBuffer<byte> meshletsPrimitiveIndicesBuffer;

        public BVH BVH;
        public ModelManager()
        {
            drawCommandBuffer = new BBG.TypedBuffer<BBG.DrawElementsIndirectCommand>();
            meshBuffer = new BBG.TypedBuffer<GpuMesh>();
            meshInstanceBuffer = new BBG.TypedBuffer<GpuMeshInstance>();
            visibleMeshInstanceBuffer = new BBG.TypedBuffer<uint>();
            materialBuffer = new BBG.TypedBuffer<GpuMaterial>();
            vertexBuffer = new BBG.TypedBuffer<GpuVertex>();
            vertexPositionBuffer = new BBG.TypedBuffer<Vector3>();
            vertexIndicesBuffer = new BBG.TypedBuffer<uint>();
            meshletTasksCmdsBuffer = new BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV>();
            meshletTasksCountBuffer = new BBG.TypedBuffer<int>();
            meshletBuffer = new BBG.TypedBuffer<GpuMeshlet>();
            meshletInfoBuffer = new BBG.TypedBuffer<GpuMeshletInfo>();
            meshletsVertexIndicesBuffer = new BBG.TypedBuffer<uint>();
            meshletsPrimitiveIndicesBuffer = new BBG.TypedBuffer<byte>();

            drawCommandBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 0);
            meshBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 1);
            meshInstanceBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 2);
            visibleMeshInstanceBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 3);
            materialBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 9);
            vertexBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 10);
            vertexPositionBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 11);
            meshletTasksCmdsBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 12);
            meshletTasksCountBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 13);
            meshletBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 14);
            meshletInfoBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 15);
            meshletsVertexIndicesBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 16);
            meshletsPrimitiveIndicesBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 17);

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
                    ref BBG.DrawElementsIndirectCommand newDrawCmd = ref DrawCommands[j];
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
                ReadOnlyMemory<BBG.DrawElementsIndirectCommand> newDrawCommands = new ReadOnlyMemory<BBG.DrawElementsIndirectCommand>(DrawCommands, prevDrawCommandsLength, DrawCommands.Length - prevDrawCommandsLength);
                BVH.AddMeshes(newDrawCommands, DrawCommands, MeshInstances, VertexPositions, VertexIndices);

                // Adjust root node index in context of all Nodes
                uint bvhNodesExclusiveSum = 0;
                for (int i = 0; i < DrawCommands.Length; i++)
                {
                    Meshes[i].BlasRootNodeIndex = bvhNodesExclusiveSum;
                    bvhNodesExclusiveSum += (uint)BVH.Tlas.Blases[i].Nodes.Length;
                }

            }

            UploadAllModelData();
            meshInstanceShouldUpload = new BitArray(MeshInstances.Length, false);
        }

        public unsafe void Draw()
        {
            BBG.Rendering.SetVertexInputAssembly(new BBG.Rendering.VertexInputAssembly()
            {
                IndexBuffer = vertexIndicesBuffer,
            });
            BBG.Rendering.MultiDrawIndexed(drawCommandBuffer, BBG.Rendering.Topology.Triangles, BBG.Rendering.IndexType.Uint, Meshes.Length, sizeof(BBG.DrawElementsIndirectCommand));
        }

        /// <summary>
        /// Requires GL_NV_mesh_shader
        /// </summary>
        public unsafe void MeshShaderDrawNV()
        {
            int maxMeshlets = meshletTasksCmdsBuffer.NumElements;
            BBG.Rendering.MultiDrawMeshletsCountNV(meshletTasksCmdsBuffer, meshletTasksCountBuffer, maxMeshlets, sizeof(BBG.DrawMeshTasksIndirectCommandNV));
        }

        private static BitArray meshInstanceShouldUpload;
        public void Update(out bool anyMeshInstanceMoved)
        {
            anyMeshInstanceMoved = false;

            int batchedUploadSize = 1 << 8;
            for (int i = 0; i < MeshInstances.Length;)
            {
                bool uploadBatch = false;
                if (meshInstanceShouldUpload[i])
                {
                    meshInstanceShouldUpload[i] = false;
                    uploadBatch = true;
                }
                else if (MeshInstances[i].DidMove())
                {
                    // We'll need to upload the changed prev matrix next frame
                    meshInstanceShouldUpload[i] = true;
                    uploadBatch = true;
                }

                if (uploadBatch)
                {
                    int batchStart = i;
                    int batchEnd = Math.Min(MyMath.NextMultiple(i, batchedUploadSize), MeshInstances.Length);

                    UpdateMeshInstanceBuffer(batchStart, batchEnd - batchStart);
                    for (int j = batchStart; j < batchEnd; j++)
                    {
                        MeshInstances[j].SetPrevToCurrentMatrix();
                    }

                    i = batchEnd;
                    anyMeshInstanceMoved = true;
                }
                else
                {
                    i++;
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

        public void ResetInstanceCounts(int count = 0)
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

        public unsafe void UploadAllModelData()
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

            BVH.Dispose();
        }
    }
}
