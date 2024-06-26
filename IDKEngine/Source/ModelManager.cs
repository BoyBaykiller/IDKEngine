﻿using System;
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

            drawCommandBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 0);
            meshBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 1);
            meshInstanceBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 2);
            visibleMeshInstanceBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 3);
            materialBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 9);
            vertexBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 10);
            vertexPositionBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 11);
            meshletTasksCmdsBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 12);
            meshletTasksCountBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 13);
            meshletBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 14);
            meshletInfoBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 15);
            meshletsVertexIndicesBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 16);
            meshletsPrimitiveIndicesBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 17);

            BVH = new BVH();
        }

        public void Add(params ModelLoader.Model?[] models)
        {
            if (models.Length == 0)
            {
                return;
            }

            int prevDrawCommandsLength = DrawCommands.Length;

            for (int i = 0; i < models.Length; i++)
            {
                if (!models[i].HasValue)
                {
                    continue;
                }

                ModelLoader.Model model = models[i].Value;

                // Upon deletion these are the properties that need to be adjusted
                Helper.ArrayAdd(ref DrawCommands, model.DrawCommands);
                for (int j = DrawCommands.Length - model.DrawCommands.Length; j < DrawCommands.Length; j++)
                {
                    ref BBG.DrawElementsIndirectCommand newDrawCmd = ref DrawCommands[j];
                    newDrawCmd.BaseInstance += MeshInstances.Length;
                    newDrawCmd.BaseVertex += Vertices.Length;
                    newDrawCmd.FirstIndex += VertexIndices.Length;
                }
                Helper.ArrayAdd(ref MeshInstances, model.MeshInstances);
                for (int j = MeshInstances.Length - model.MeshInstances.Length; j < MeshInstances.Length; j++)
                {
                    ref GpuMeshInstance newMeshInstance = ref MeshInstances[j];
                    newMeshInstance.MeshIndex += Meshes.Length;
                }
                Helper.ArrayAdd(ref Meshes, model.Meshes);
                for (int j = Meshes.Length - model.Meshes.Length; j < Meshes.Length; j++)
                {
                    ref GpuMesh newMesh = ref Meshes[j];
                    newMesh.MaterialIndex += Materials.Length;
                    newMesh.MeshletsStart += Meshlets.Length;
                }
                Helper.ArrayAdd(ref Meshlets, model.Meshlets);
                for (int j = Meshlets.Length - model.Meshlets.Length; j < Meshlets.Length; j++)
                {
                    ref GpuMeshlet newMeshlet = ref Meshlets[j];
                    newMeshlet.VertexOffset += (uint)MeshletsVertexIndices.Length;
                    newMeshlet.IndicesOffset += (uint)MeshletsLocalIndices.Length;
                }

                Helper.ArrayAdd(ref Materials, model.Materials);

                Helper.ArrayAdd(ref Vertices, model.Vertices);
                Helper.ArrayAdd(ref VertexPositions, model.VertexPositions);
                Helper.ArrayAdd(ref VertexIndices, model.VertexIndices);

                Helper.ArrayAdd(ref MeshletTasksCmds, model.MeshletTasksCmds);
                Helper.ArrayAdd(ref MeshletsInfo, model.MeshletsInfo);
                Helper.ArrayAdd(ref MeshletsVertexIndices, model.MeshletsVertexIndices);
                Helper.ArrayAdd(ref MeshletsLocalIndices, model.MeshletsLocalIndices);
            }

            {
                ReadOnlySpan<BBG.DrawElementsIndirectCommand> newDrawCommands = new ReadOnlySpan<BBG.DrawElementsIndirectCommand>(DrawCommands, prevDrawCommandsLength, DrawCommands.Length - prevDrawCommandsLength);
                BVH.AddMeshes(newDrawCommands, VertexPositions, VertexIndices, DrawCommands, MeshInstances);

                // Adjust root node index in context of all Nodes
                uint bvhNodesExclusiveSum = 0;
                for (int i = 0; i < DrawCommands.Length; i++)
                {
                    Meshes[i].BlasRootNodeOffset = bvhNodesExclusiveSum;
                    bvhNodesExclusiveSum += (uint)BVH.Tlas.Blases[i].Nodes.Length;
                }

            }

            UploadAllModelData();
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

        public void Update(out bool anyMeshInstanceMoved)
        {
            anyMeshInstanceMoved = false;

            int batchedUploadSize = 1 << 8;
            for (int i = 0; i < MeshInstances.Length;)
            {
                bool uploadBatch = false;
                if (MeshInstances[i].IsDirty)
                {
                    uploadBatch = true;
                }

                if (uploadBatch)
                {
                    int batchStart = i;
                    int batchEnd = Math.Min(MyMath.NextMultiple(i, batchedUploadSize), MeshInstances.Length);

                    UpdateMeshInstanceBuffer(batchStart, batchEnd - batchStart);
                    for (int j = batchStart; j < batchEnd; j++)
                    {
                        if (MeshInstances[j].DidMove())
                        {
                            MeshInstances[j].SetPrevToCurrentMatrix();
                            anyMeshInstanceMoved = true;
                        }
                    }

                    i = batchEnd;
                }
                else
                {
                    i++;
                }
            }
        }

        public void UpdateMeshBuffer(int start, int count)
        {
            if (count == 0) return;
            meshBuffer.UploadElements(start, count, Meshes[start]);
        }

        public void UpdateDrawCommandBuffer(int start, int count)
        {
            if (count == 0) return;
            drawCommandBuffer.UploadElements(start, count, DrawCommands[start]);
        }

        public void UpdateMeshInstanceBuffer(int start, int count)
        {
            meshInstanceBuffer.UploadElements(start, count, MeshInstances[start]);
            for (int i = start; i < start + count; i++)
            {
                MeshInstances[i].ResetDirtyFlag();
            }
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
