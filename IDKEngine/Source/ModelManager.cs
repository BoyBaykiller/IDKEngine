using System;
using System.Linq;
using System.Threading;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Bvh;
using IDKEngine.Utils;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public unsafe class ModelManager : IDisposable
    {
        public record struct CpuModel
        {
            public readonly ModelLoader.Node Root => Nodes[0];
            public readonly string Name => Root.Name;

            public ModelLoader.Node[] Nodes;
            public ModelLoader.ModelAnimation[] Animations;
            public BitArray EnabledAnimations;
        }

        private record struct SkinningCmd
        {
            /// <summary>
            /// The node with a Skin
            /// </summary>
            public ModelLoader.Node SkinnedNode;

            /// <summary>
            /// The offset into the buffer containing animated vertices for the first mesh in <see cref="SkinnedNode"/>
            /// </summary>
            public int VertexOffset;

            public int JointMatrixOffset;
        }

        public CpuModel[] CpuModels = [];
        public BVH BVH;

        public BBG.DrawElementsIndirectCommand[] DrawCommands = [];
        public ReadOnlyArray<GpuMeshInstance> MeshInstances => new ReadOnlyArray<GpuMeshInstance>(meshInstances);
        public ModelLoader.CpuMaterial[] CpuMaterials = [];
        public GpuMaterial[] GpuMaterials = [];
        public GpuMesh[] Meshes = [];
        public GpuVertex[] Vertices = [];
        public NativeMemoryView<Vector3> VertexPositions;
        public uint[] VertexIndices = [];
        public Matrix3x4[] JointMatrices = [];
        public BBG.TypedBuffer<uint> OpaqueMeshInstanceIdBuffer;
        public BBG.TypedBuffer<uint> TransparentMeshInstanceIdBuffer;

        private GpuMeshInstance[] meshInstances = [];
        private BitArray meshInstancesDirty;

        private BBG.TypedBuffer<BBG.DrawElementsIndirectCommand> drawCommandBuffer;
        private BBG.TypedBuffer<GpuMesh> meshesBuffer;
        private BBG.TypedBuffer<GpuMeshInstance> meshInstanceBuffer;
        private BBG.TypedBuffer<GpuMaterial> materialsBuffer;
        private BBG.TypedBuffer<GpuVertex> vertexBuffer;
        private BBG.TypedBuffer<Vector3> vertexPositionsBuffer;
        private BBG.TypedBuffer<Vector3> vertexPositionsHostBuffer;
        private BBG.TypedBuffer<GpuUnskinnedVertex> unskinnedVerticesBuffer;
        private BBG.TypedBuffer<uint> vertexIndicesBuffer;
        private BBG.TypedBuffer<Matrix3x4> jointMatricesBuffer;
        private BBG.TypedBuffer<Vector3> prevVertexPositionsBuffer;
        private BBG.TypedBuffer<uint> visibleMeshInstanceIdBuffer;
        private BBG.TypedBuffer<GpuMeshlet> meshletBuffer;
        private BBG.TypedBuffer<GpuMeshletInfo> meshletInfoBuffer;
        private BBG.TypedBuffer<uint> meshletsVertexIndicesBuffer;
        private BBG.TypedBuffer<byte> meshletsPrimitiveIndicesBuffer;
        private BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV> meshletTasksCmdsBuffer;
        private readonly BBG.TypedBuffer<uint> meshletTasksCountBuffer;

        private readonly BBG.AbstractShaderProgram skinningShaderProgram;
        private BBG.Fence? fenceCopiedSkinnedVerticesToHost;

        private SkinningCmd[] skinningCmds;

        private bool runSkinningShader;
        private float prevAnimationTime = float.MinValue;

        public ModelManager()
        {
            drawCommandBuffer = new BBG.TypedBuffer<BBG.DrawElementsIndirectCommand>();
            meshesBuffer = new BBG.TypedBuffer<GpuMesh>();
            meshInstanceBuffer = new BBG.TypedBuffer<GpuMeshInstance>();
            visibleMeshInstanceIdBuffer = new BBG.TypedBuffer<uint>();
            materialsBuffer = new BBG.TypedBuffer<GpuMaterial>();
            vertexBuffer = new BBG.TypedBuffer<GpuVertex>();
            vertexPositionsBuffer = new BBG.TypedBuffer<Vector3>();
            vertexIndicesBuffer = new BBG.TypedBuffer<uint>();
            meshletTasksCmdsBuffer = new BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV>();
            meshletTasksCountBuffer = new BBG.TypedBuffer<uint>();
            meshletBuffer = new BBG.TypedBuffer<GpuMeshlet>();
            meshletInfoBuffer = new BBG.TypedBuffer<GpuMeshletInfo>();
            meshletsVertexIndicesBuffer = new BBG.TypedBuffer<uint>();
            meshletsPrimitiveIndicesBuffer = new BBG.TypedBuffer<byte>();
            jointMatricesBuffer = new BBG.TypedBuffer<Matrix3x4>();
            unskinnedVerticesBuffer = new BBG.TypedBuffer<GpuUnskinnedVertex>();
            vertexPositionsHostBuffer = new BBG.TypedBuffer<Vector3>();
            prevVertexPositionsBuffer = new BBG.TypedBuffer<Vector3>();
            OpaqueMeshInstanceIdBuffer = new BBG.TypedBuffer<uint>();
            TransparentMeshInstanceIdBuffer = new BBG.TypedBuffer<uint>();

            drawCommandBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 1);
            meshesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 2);
            meshInstanceBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 3);
            visibleMeshInstanceIdBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 4);
            materialsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 5);
            vertexBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 6);
            vertexPositionsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 7);
            meshletTasksCmdsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 8);
            meshletTasksCountBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 9);
            meshletBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 10);
            meshletInfoBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 11);
            meshletsVertexIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 12);
            meshletsPrimitiveIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 13);
            jointMatricesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 14);
            unskinnedVerticesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 15);
            prevVertexPositionsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 16);

            meshletTasksCountBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);

            BVH = new BVH();

            skinningShaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "Skinning/compute.glsl"));
        }

        public void Add(params ReadOnlySpan<ModelLoader.Model> models)
        {
            vertexPositionsHostBuffer.DownloadElements(out Vector3[] vertexPositions);
            meshletBuffer.DownloadElements(out GpuMeshlet[] meshlets);
            meshletInfoBuffer.DownloadElements(out GpuMeshletInfo[] meshletsInfo);
            meshletsVertexIndicesBuffer.DownloadElements(out uint[] meshletsVertexIndices);
            meshletsPrimitiveIndicesBuffer.DownloadElements(out byte[] meshletsPrimitiveIndices);
            unskinnedVerticesBuffer.DownloadElements(out GpuUnskinnedVertex[] unskinnedVertices);

            int prevDrawCommandsLength = DrawCommands.Length;
            for (int i = 0; i < models.Length; i++)
            {
                ref readonly ModelLoader.Model model = ref models[i];
                ref readonly ModelLoader.GpuModel gpuModel = ref model.GpuModel;
                CpuModel myModel = ConvertToCpuModel(model);

                // The following data needs to be readjusted when deleting models as it has offsets backed in

                for (int j = 0; j < myModel.Nodes.Length; j++)
                {
                    ModelLoader.Node node = myModel.Nodes[j];
                    if (node.HasMeshInstances)
                    {
                        node.MeshInstanceRange.Start += meshInstances.Length;
                    }
                }
                Helper.ArrayAdd(ref CpuModels, myModel);

                Helper.ArrayAdd(ref DrawCommands, gpuModel.DrawCommands);
                for (int j = DrawCommands.Length - gpuModel.DrawCommands.Length; j < DrawCommands.Length; j++)
                {
                    ref BBG.DrawElementsIndirectCommand newDrawCmd = ref DrawCommands[j];
                    newDrawCmd.BaseInstance += meshInstances.Length;
                    newDrawCmd.BaseVertex += Vertices.Length;
                    newDrawCmd.FirstIndex += VertexIndices.Length;
                }

                Helper.ArrayAdd(ref meshInstances, gpuModel.MeshInstances);
                for (int j = meshInstances.Length - gpuModel.MeshInstances.Length; j < meshInstances.Length; j++)
                {
                    ref GpuMeshInstance newMeshInstance = ref meshInstances[j];
                    newMeshInstance.MeshId += Meshes.Length;
                }

                Helper.ArrayAdd(ref Meshes, gpuModel.Meshes);
                for (int j = Meshes.Length - gpuModel.Meshes.Length; j < Meshes.Length; j++)
                {
                    ref GpuMesh newMesh = ref Meshes[j];
                    newMesh.MaterialId += GpuMaterials.Length;
                    newMesh.MeshletsOffset += meshlets.Length;
                }

                Helper.ArrayAdd(ref meshlets, gpuModel.Meshlets);
                for (int j = meshlets.Length - gpuModel.Meshlets.Length; j < meshlets.Length; j++)
                {
                    ref GpuMeshlet newMeshlet = ref meshlets[j];
                    newMeshlet.VertexOffset += (uint)meshletsVertexIndices.Length;
                    newMeshlet.IndicesOffset += (uint)meshletsPrimitiveIndices.Length;
                }

                Helper.ArrayAdd(ref CpuMaterials, model.Materials);
                Helper.ArrayAdd(ref GpuMaterials, gpuModel.Materials);
                Helper.ArrayAdd(ref Vertices, gpuModel.Vertices);
                Helper.ArrayAdd(ref vertexPositions, gpuModel.VertexPositions);
                Helper.ArrayAdd(ref VertexIndices, gpuModel.VertexIndices);
                Helper.ArrayAdd(ref meshletsInfo, gpuModel.MeshletsInfo);
                Helper.ArrayAdd(ref meshletsVertexIndices, gpuModel.MeshletsVertexIndices);
                Helper.ArrayAdd(ref meshletsPrimitiveIndices, gpuModel.MeshletsLocalIndices);
                Helper.ArrayAdd(ref unskinnedVertices, LoadUnskinnedVertices(model));
            }

            skinningCmds = GetSkinningCommands(out int numJoints, vertexPositions);
            Array.Resize(ref JointMatrices, numJoints);

            UpdateBuffers(vertexPositions, meshlets, meshletsInfo, meshletsVertexIndices, meshletsPrimitiveIndices, unskinnedVertices);

            ReadOnlySpan<BBG.DrawElementsIndirectCommand> newDrawCommands = new ReadOnlySpan<BBG.DrawElementsIndirectCommand>(DrawCommands, prevDrawCommandsLength, DrawCommands.Length - prevDrawCommandsLength);
            BVH.GeometryDesc[] geometriesDesc = new BVH.GeometryDesc[newDrawCommands.Length];
            for (int i = 0; i < geometriesDesc.Length; i++)
            {
                ref readonly BBG.DrawElementsIndirectCommand cmd = ref newDrawCommands[i];

                BVH.GeometryDesc geometryDesc = new BVH.GeometryDesc();
                geometryDesc.TriangleCount = cmd.IndexCount / 3;
                geometryDesc.TriangleOffset = cmd.FirstIndex / 3;
                geometryDesc.VertexOffset = cmd.BaseVertex;
                geometryDesc.VertexCount = GetMeshesVertexCount(prevDrawCommandsLength + i);

                geometriesDesc[i] = geometryDesc;
            }
            BVH.Add(geometriesDesc);
            BVH.SetSourceGeometry(VertexPositions, VertexIndices);
            BVH.SetSourceInstances(meshInstances);
            BVH.BlasesBuild(BVH.BlasesDesc.Length - geometriesDesc.Length, geometriesDesc.Length);
            BVH.TlasBuild(true);

            for (int i = 0; i < DrawCommands.Length; i++)
            {
                Meshes[i].BlasRootNodeOffset = BVH.BlasesDesc[i].RootNodeOffset;
            }
            UploadMeshBuffer(0, Meshes.Length);

            UpdateOpqauesAndTransparents();
            meshInstancesDirty = new BitArray(meshInstances.Length, true);
        }

        public void RemoveMesh(Range rmMeshRange)
        {
            if (rmMeshRange.Count == 0) return;

            vertexPositionsHostBuffer.DownloadElements(out Vector3[] vertexPositions);
            meshletBuffer.DownloadElements(out GpuMeshlet[] meshlets);
            meshletInfoBuffer.DownloadElements(out GpuMeshletInfo[] meshletsInfo);
            meshletsVertexIndicesBuffer.DownloadElements(out uint[] meshletsVertexIndices);
            meshletsPrimitiveIndicesBuffer.DownloadElements(out byte[] meshletsPrimitiveIndices);
            unskinnedVerticesBuffer.DownloadElements(out GpuUnskinnedVertex[] unskinnedVertices);

            {
                int rangeStart = int.MaxValue;
                int rangeEnd = 0;
                bool overlapsWithSkinnedMeshes = false;
                for (int i = skinningCmds.Length - 1; i >= 0; i--)
                {
                    SkinningCmd cmd = skinningCmds[i];
                    ModelLoader.Node node = cmd.SkinnedNode;
                    Range nodeMeshRange = GetNodeMeshRange(node);

                    if (nodeMeshRange.Overlaps(rmMeshRange, out Range overlapRange))
                    {
                        int rmVertexCount = GetMeshesVertexCount(overlapRange.Start, overlapRange.Count);
                        int unskinnedVerticesOffset = cmd.VertexOffset + GetMeshesVertexCount(DrawCommands, vertexPositions, nodeMeshRange.Start, overlapRange.Start - nodeMeshRange.Start);

                        if (!overlapsWithSkinnedMeshes)
                        {
                            rangeEnd = Math.Max(rangeEnd, unskinnedVerticesOffset + rmVertexCount);
                            overlapsWithSkinnedMeshes = true;
                        }
                        rangeStart = Math.Min(rangeStart, unskinnedVerticesOffset);

                        bool deleteSkin = nodeMeshRange.Count - overlapRange.Count == 0;
                        for (int j = i + 1; j < skinningCmds.Length; j++)
                        {
                            ref SkinningCmd otherCmd = ref skinningCmds[j];
                            otherCmd.VertexOffset -= rmVertexCount;
                            if (deleteSkin)
                            {
                                otherCmd.JointMatrixOffset -= node.Skin.Joints.Length;
                            }
                        }

                        if (deleteSkin)
                        {
                            Helper.ArrayRemove(ref JointMatrices, cmd.JointMatrixOffset, node.Skin.Joints.Length);
                            Helper.ArrayRemove(ref skinningCmds, i, 1);
                            node.Skin = new ModelLoader.Skin();
                        }
                    }
                }
                if (overlapsWithSkinnedMeshes)
                {
                    Helper.ArrayRemove(ref unskinnedVertices, rangeStart, rangeEnd - rangeStart);
                }
            }

            Range rmIndicesRange = GetMeshesIndicesRange(rmMeshRange);
            Helper.ArrayRemove(ref VertexIndices, rmIndicesRange.Start, rmIndicesRange.Count);

            Range rmVerticesRange = GetMeshesVerticesRange(rmMeshRange);
            Helper.ArrayRemove(ref Vertices, rmVerticesRange.Start, rmVerticesRange.Count);
            Helper.ArrayRemove(ref vertexPositions, rmVerticesRange.Start, rmVerticesRange.Count);

            Range rmInstanceRange = GetMeshesInstanceRange(rmMeshRange);
            for (int i = rmInstanceRange.End; i < meshInstances.Length; i++)
            {
                ref GpuMeshInstance meshInstance = ref meshInstances[i];
                meshInstance.MeshId -= rmMeshRange.Count;
            }
            RemoveMeshInstancesImpl(rmInstanceRange);

            for (int i = rmMeshRange.End; i < DrawCommands.Length; i++)
            {
                ref BBG.DrawElementsIndirectCommand cmd = ref DrawCommands[i];
                cmd.BaseVertex -= rmVerticesRange.Count;
                cmd.FirstIndex -= rmIndicesRange.Count;
            }

            Helper.ArrayRemove(ref DrawCommands, rmMeshRange.Start, rmMeshRange.Count);
            
            SortedSet<int> freeListMaterials = new SortedSet<int>();
            for (int meshId = rmMeshRange.Start; meshId < rmMeshRange.End; meshId++)
            {
                ref readonly GpuMesh rmMesh = ref Meshes[meshId];

                bool removeMaterial = !IsMaterialReferenced(rmMesh.MaterialId, Meshes, rmMeshRange);
                if (removeMaterial)
                {
                    freeListMaterials.Add(rmMesh.MaterialId);
                }
            }
            Helper.ArrayRemove(ref Meshes, rmMeshRange.Start, rmMeshRange.Count);
            
            foreach (int rmMaterialId in freeListMaterials.Reverse())
            {
                GpuMaterial rmGpuMaterial = GpuMaterials[rmMaterialId];
                ModelLoader.CpuMaterial rmCpuMaterial = CpuMaterials[rmMaterialId];

                for (int i = 0; i < GpuMaterial.TEXTURE_COUNT; i++)
                {
                    BBG.Texture.BindlessHandle handle = rmGpuMaterial[(GpuMaterial.TextureType)i];

                    if (!IsTextureHandleReferenced(handle, GpuMaterials, freeListMaterials))
                    {
                        rmCpuMaterial.SampledTextures[i].Dispose();
                    }
                }
                
                for (int i = 0; i < Meshes.Length; i++)
                {
                    ref GpuMesh mesh = ref Meshes[i];
                    if (mesh.MaterialId > rmMaterialId)
                    {
                        mesh.MaterialId--;
                    }
                }
            }
            
            foreach (int rmMaterialId in freeListMaterials.Reverse())
            {
                Helper.ArrayRemove(ref GpuMaterials, rmMaterialId, 1);
                Helper.ArrayRemove(ref CpuMaterials, rmMaterialId, 1);
            }

            UpdateBuffers(vertexPositions, meshlets, meshletsInfo, meshletsVertexIndices, meshletsPrimitiveIndices, unskinnedVertices);
            
            BVH.RemoveBlas(rmMeshRange, VertexPositions, VertexIndices, meshInstances);
            for (int i = rmMeshRange.Start; i < Meshes.Length; i++)
            {
                ref GpuMesh mesh = ref Meshes[i];
                mesh.BlasRootNodeOffset = BVH.BlasesDesc[i].RootNodeOffset;
            }
            UploadMeshBuffer(0, Meshes.Length);

            UpdateOpqauesAndTransparents();
            meshInstancesDirty = new BitArray(meshInstances.Length, true);
            fenceCopiedSkinnedVerticesToHost?.Dispose();
            fenceCopiedSkinnedVerticesToHost = null;
            runSkinningShader = true;
        }

        public void RemoveMeshInstances(Range rmInstanceRange)
        {
            int sameMeshCounter = 0;
            for (int i = rmInstanceRange.End - 1; i >= rmInstanceRange.Start; i--)
            {
                ref readonly GpuMeshInstance meshInstance = ref meshInstances[i];

                if (meshInstances[rmInstanceRange.End - 1].MeshId == meshInstance.MeshId)
                {
                    sameMeshCounter++;
                }
                else
                {
                    // delete the discovered range of mesh instances with the same meshId
                    if (DrawCommands[meshInstance.MeshId + 1].InstanceCount - sameMeshCounter == 0)
                    {
                        RemoveMesh(new Range(meshInstance.MeshId + 1, 1));
                    }
                    else
                    {
                        RemoveMeshInstancesImpl(new Range(rmInstanceRange.End - sameMeshCounter, sameMeshCounter));
                    }
                    sameMeshCounter = 1;
                    rmInstanceRange.End = i + 1;
                }

                if (i == rmInstanceRange.Start)
                {
                    // last iteration
                    if (DrawCommands[meshInstance.MeshId].InstanceCount - sameMeshCounter == 0)
                    {
                        RemoveMesh(new Range(meshInstance.MeshId, 1));
                    }
                    else
                    {
                        RemoveMeshInstancesImpl(new Range(rmInstanceRange.End - sameMeshCounter, sameMeshCounter));
                    }
                }
            }
            UpdateBuffers();

            BVH.SetSourceInstances(meshInstances);

            meshInstancesDirty = new BitArray(meshInstances.Length, true);
            UpdateOpqauesAndTransparents();
        }

        public void UpdateOpqauesAndTransparents()
        {
            List<uint> opaqueMeshInstanceIds = new List<uint>();
            List<uint> transparentMeshInstanceIds = new List<uint>();
            for (uint i = 0; i < meshInstances.Length; i++)
            {
                ref readonly GpuMeshInstance meshInstance = ref meshInstances[i];
                ref readonly GpuMesh mesh = ref Meshes[meshInstance.MeshId];

                //transparentMeshInstanceIds.Add(i);
                //continue;

                if (GpuMaterials[mesh.MaterialId].IsTransparent())
                {
                    transparentMeshInstanceIds.Add(i);
                }
                else
                {
                    opaqueMeshInstanceIds.Add(i);
                }
            }
            BBG.Buffer.Recreate(ref OpaqueMeshInstanceIdBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, CollectionsMarshal.AsSpan(opaqueMeshInstanceIds));
            BBG.Buffer.Recreate(ref TransparentMeshInstanceIdBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, CollectionsMarshal.AsSpan(transparentMeshInstanceIds));
        }

        public void Draw()
        {
            BBG.Rendering.SetVertexInputAssembly(new BBG.Rendering.VertexInputDesc()
            {
                IndexBuffer = vertexIndicesBuffer,
            });
            BBG.Rendering.MultiDrawIndexed(drawCommandBuffer, BBG.Rendering.Topology.Triangles, BBG.Rendering.IndexType.Uint, Meshes.Length, sizeof(BBG.DrawElementsIndirectCommand));
        }

        /// <summary>
        /// Requires GL_NV_mesh_shader
        /// </summary>
        public void MeshShaderDrawNV()
        {
            int maxMeshlets = meshletTasksCmdsBuffer.NumElements;
            BBG.Rendering.MultiDrawMeshletsCountNV(meshletTasksCmdsBuffer, meshletTasksCountBuffer, maxMeshlets, sizeof(BBG.DrawMeshTasksIndirectCommandNV));
        }

        public void Update(float animationsTime, out bool anyNodeMoved, out bool anyMeshInstanceMoved)
        {
            UpdateNodeAnimations(animationsTime);
            UpdateNodeHierarchy(out anyNodeMoved);
            UpdateMeshInstanceBufferBatched(out anyMeshInstanceMoved);
            ComputeSkinnedPositions(anyNodeMoved);

            // Only need to update BLAS when node was animated, could use more granular check
            if (anyNodeMoved)
            {
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    ref readonly SkinningCmd cmd = ref skinningCmds[i];
                    Range meshRange = GetNodeMeshRange(cmd.SkinnedNode);
                    BVH.GpuBlasesRefit(meshRange.Start, meshRange.Count);
                }
            }

            // Only need to update TLAS if a BLAS was moved or animated
            if (anyMeshInstanceMoved || anyNodeMoved)
            {
                BVH.TlasBuild();
            }
        }

        public void ComputeSkinnedPositions(bool anyAnimatedNodeMoved)
        {
            if (anyAnimatedNodeMoved)
            {
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    ref readonly SkinningCmd skinningCmd = ref skinningCmds[i];
                    ref readonly ModelLoader.Skin skin = ref skinningCmd.SkinnedNode.Skin;

                    Matrix4 inverseNodeTransform = Matrix4.Invert(skinningCmd.SkinnedNode.GlobalTransform);
                    for (int j = 0; j < skin.Joints.Length; j++)
                    {
                        JointMatrices[skinningCmd.JointMatrixOffset + j] = MyMath.Matrix4x4ToTranposed3x4(skin.InverseJointMatrices[j] * skin.Joints[j].GlobalTransform * inverseNodeTransform);
                    }
                }
                jointMatricesBuffer.UploadElements(JointMatrices);
            }

            if (fenceCopiedSkinnedVerticesToHost.HasValue && fenceCopiedSkinnedVerticesToHost.Value.TryWait())
            {
                // Read the skinned vertices with one frame delay to avoid sync
                // and refit their BLASes. Refitting on GPU is done elsewhere.

                int threadPoolThreads = Math.Max(Environment.ProcessorCount / 2, 1);
                ThreadPool.SetMinThreads(threadPoolThreads, 1);
                ThreadPool.SetMaxThreads(threadPoolThreads, 1);

                Task[] tasks = new Task[skinningCmds.Sum(it => GetNodeMeshRange(it.SkinnedNode).Count)];
                int taskCounter = 0;
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    SkinningCmd skinningCmd = skinningCmds[i];
                    Range meshRange = GetNodeMeshRange(skinningCmd.SkinnedNode);

                    int firstBaseVertex = DrawCommands[meshRange.Start].BaseVertex;
                    for (int j = meshRange.Start; j < meshRange.End; j++)
                    {
                        BVH.BlasDesc blasDesc = BVH.BlasesDesc[j];

                        int baseVertex = DrawCommands[j].BaseVertex;
                        int vertexCount = GetMeshesVertexCount(j);
                        int localSkinningVertexOffset = baseVertex - firstBaseVertex;

                        tasks[taskCounter++] = Task.Run(() =>
                        {
                            BVH.CpuBlasRefit(blasDesc);
                        });
                    }
                }
                Task.WaitAll(tasks);

                fenceCopiedSkinnedVerticesToHost.Value.Dispose();
                fenceCopiedSkinnedVerticesToHost = null;
            }

            if (anyAnimatedNodeMoved)
            {
                runSkinningShader = true;
            }

            if (runSkinningShader)
            {
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    SkinningCmd skinningCmd = skinningCmds[i];
                    Range meshRange = GetNodeMeshRange(skinningCmd.SkinnedNode);

                    int outputVertexOffset = DrawCommands[meshRange.Start].BaseVertex;
                    int vertexCount = GetMeshesVertexCount(meshRange.Start, meshRange.Count);

                    BBG.Computing.Compute("Compute Skinned vertices", () =>
                    {
                        skinningShaderProgram.Upload(0, (uint)skinningCmd.VertexOffset);
                        skinningShaderProgram.Upload(1, (uint)outputVertexOffset);
                        skinningShaderProgram.Upload(2, (uint)skinningCmd.JointMatrixOffset);
                        skinningShaderProgram.Upload(3, (uint)vertexCount);

                        BBG.Cmd.UseShaderProgram(skinningShaderProgram);
                        BBG.Computing.Dispatch(MyMath.DivUp(vertexCount, 64), 1, 1);
                    });
                    vertexPositionsBuffer.CopyElementsTo(vertexPositionsHostBuffer, outputVertexOffset, outputVertexOffset, vertexCount);
                }
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);

                // When signaled all skinned vertices have been computed and copied into the host buffer
                fenceCopiedSkinnedVerticesToHost = BBG.Fence.InsertIntoCommandStream();

                if (!anyAnimatedNodeMoved)
                {
                    // Skinning shader must stop with one frame delay after Animations have been stoped so velocity becomes zero
                    runSkinningShader = false;
                }
            }
        }

        public void SetMeshInstance(int index, in GpuMeshInstance meshInstance)
        {
            meshInstances[index] = meshInstance;
            meshInstancesDirty[index] = true;
        }

        public void ResetInstanceCounts(int count = 0)
        {
            // for vertex rendering path
            for (int i = 0; i < DrawCommands.Length; i++)
            {
                DrawCommands[i].InstanceCount = count;
            }
            UploadDrawCommandBuffer(0, DrawCommands.Length);
            for (int i = 0; i < DrawCommands.Length; i++)
            {
                ref readonly GpuMesh mesh = ref Meshes[i];
                DrawCommands[i].InstanceCount = mesh.InstanceCount;
            }

            // for mesh-shader rendering path
            meshletTasksCountBuffer.UploadElements((uint)count);
        }

        public int GetMeshesVertexCount(int startMesh, int count = 1)
        {
            return GetMeshesVertexCount(DrawCommands, VertexPositions, startMesh, count);
        }

        public Range GetMeshesVerticesRange(Range meshes)
        {
            return new Range(DrawCommands[meshes.Start].BaseVertex, GetMeshesVertexCount(meshes.Start, meshes.Count));
        }

        public Range GetMeshesInstanceRange(Range meshes)
        {
            Range range = new Range();
            range.Start = DrawCommands[meshes.Start].BaseInstance;
            range.End = DrawCommands[meshes.End - 1].BaseInstance + DrawCommands[meshes.End - 1].InstanceCount;

            return range;
        }

        public Range GetMeshesIndicesRange(Range meshes)
        {
            Range range = new Range();
            range.Start = DrawCommands[meshes.Start].FirstIndex;
            range.End = DrawCommands[meshes.End - 1].FirstIndex + DrawCommands[meshes.End - 1].IndexCount;

            return range;
        }

        public Range GetInstancesMeshRange(Range instanceRange)
        {
            return GetInstancesMeshRange(instanceRange, meshInstances);
        }

        public Range GetNodeMaterialRange(ModelLoader.Node node)
        {
            Range meshRange = GetNodeMeshRangeRecursive(node);
            if (meshRange.Count == 0)
            {
                return new Range(0, 0);
            }

            int min = int.MaxValue;
            int max = 0;
            for (int i = meshRange.Start; i < meshRange.End; i++)
            {
                min = Math.Min(min, Meshes[i].MaterialId);
                max = Math.Max(max, Meshes[i].MaterialId);
            }

            return new Range(min, max - min + 1);
        }

        public Range GetNodeMeshRange(ModelLoader.Node node)
        {
            return GetNodeMeshRange(node, meshInstances);
        }

        public Range GetNodeMeshRangeRecursive(ModelLoader.Node node)
        {
            int min = int.MaxValue;
            int max = -1;
            ModelLoader.Node.Traverse(node, (node) =>
            {
                if (node.HasMeshInstances)
                {
                    Range meshRange = GetNodeMeshRange(node);

                    min = Math.Min(min, meshRange.Start);
                    max = Math.Max(max, meshRange.End);
                }
            });

            if (max == -1)
            {
                return new Range(0, 0);
            }

            return new Range(min, max - min);
        }

        public void UploadVertexPositionBuffer(int start, int count)
        {
            vertexPositionsBuffer.UploadElements(start, count, VertexPositions[start]);
        }

        public void UploadMeshBuffer(int start, int count)
        {
            if (count == 0) return;
            meshesBuffer.UploadElements(start, count, Meshes[start]);
        }

        public void UploadMaterialBuffer(int start, int count)
        {
            if (count == 0) return;
            materialsBuffer.UploadElements(start, count, GpuMaterials[start]);
        }

        public void UploadDrawCommandBuffer(int start, int count)
        {
            if (count == 0) return;
            drawCommandBuffer.UploadElements(start, count, DrawCommands[start]);
        }

        public void UploadMeshInstanceBuffer(int start, int count)
        {
            meshInstanceBuffer.UploadElements(start, count, meshInstances[start]);
            for (int i = 0; i < count; i++)
            {
                meshInstancesDirty[start + i] = false;
            }
        }

        private void RemoveMeshInstancesImpl(Range rmInstanceRange)
        {
            Range meshRange = GetInstancesMeshRange(rmInstanceRange);

            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref readonly CpuModel model = ref CpuModels[i];

                for (int j = 0; j < model.Nodes.Length; j++)
                {
                    ModelLoader.Node node = model.Nodes[j];

                    if (!node.HasMeshInstances)
                    {
                        continue;
                    }

                    if (node.MeshInstanceRange.End <= rmInstanceRange.Start)
                    {
                        // mesh instances to be removed are after this nodes mesh instances
                    }
                    else if (node.MeshInstanceRange.Start >= rmInstanceRange.End)
                    {
                        // mesh instances to be removed are before this nodes mesh instances
                        node.MeshInstanceRange.Start -= rmInstanceRange.Count;
                    }
                    else if (node.MeshInstanceRange.Overlaps(rmInstanceRange, out int overlap))
                    {
                        // mesh instances to be removed overlap with this nodes mesh instances
                        node.MeshInstanceRange.Count -= overlap;
                    }
                }
            }

            for (int i = rmInstanceRange.Start; i < rmInstanceRange.End; i++)
            {
                int meshId = meshInstances[i].MeshId;

                DrawCommands[meshId].InstanceCount--;
                Meshes[meshId].InstanceCount--;
            }

            for (int i = meshRange.End; i < DrawCommands.Length; i++)
            {
                ref BBG.DrawElementsIndirectCommand cmd = ref DrawCommands[i];
                cmd.BaseInstance -= rmInstanceRange.Count;
            }

            Helper.ArrayRemove(ref meshInstances, rmInstanceRange.Start, rmInstanceRange.Count);
        }

        private void UpdateMeshInstanceBufferBatched(out bool anyMeshInstanceMoved)
        {
            anyMeshInstanceMoved = false;

            int batchedUploadSize = 1 << 8;
            int start = 0;
            int count = meshInstances.Length;
            int end = start + count;
            for (int i = start; i < end;)
            {
                if (meshInstancesDirty[i])
                {
                    int batchStart = i;
                    int batchEnd = Math.Min(MyMath.NextMultiple(i, batchedUploadSize), end);

                    UploadMeshInstanceBuffer(batchStart, batchEnd - batchStart);
                    for (int j = batchStart; j < batchEnd; j++)
                    {
                        if (meshInstances[j].DidMove())
                        {
                            meshInstances[j].SetPrevToCurrentMatrix();
                            meshInstancesDirty[j] = true;
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

        private void UpdateNodeHierarchy(out bool anyNodeMoved)
        {
            bool anyNodeMovedCopy = false;
            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref readonly CpuModel cpuModel = ref CpuModels[i];
                ModelLoader.Node.TraverseUpdate(cpuModel.Root, (ModelLoader.Node node) =>
                {
                    anyNodeMovedCopy = true;

                    Transformation nodeTransformBefore = Transformation.FromMatrix(node.GlobalTransform);
                    node.UpdateGlobalTransform();

                    if (node.HasMeshInstances)
                    {
                        Transformation nodeTransformAfter = Transformation.FromMatrix(node.GlobalTransform);
                        for (int j = node.MeshInstanceRange.Start; j < node.MeshInstanceRange.End; j++)
                        {
                            GpuMeshInstance meshInstance = MeshInstances[j];
                            Transformation transformDiff = Transformation.FromMatrix(meshInstance.ModelMatrix) - nodeTransformBefore;
                            Transformation adjustedTransform = nodeTransformAfter + transformDiff;

                            meshInstance.ModelMatrix = adjustedTransform.GetMatrix();
                            SetMeshInstance(j, meshInstance);
                        }
                    }
                });
            }

            anyNodeMoved = anyNodeMovedCopy;
        }

        private void UpdateNodeAnimations(float time)
        {
            if (time == prevAnimationTime)
            {
                return;
            }
            prevAnimationTime = time;

            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref readonly CpuModel cpuModel = ref CpuModels[i];
                for (int j = 0; j < cpuModel.Animations.Length; j++)
                {
                    if (!cpuModel.EnabledAnimations[j])
                    {
                        continue;
                    }

                    ref readonly ModelLoader.ModelAnimation modelAnimation = ref cpuModel.Animations[j];
                    float animationTime = time % modelAnimation.Duration;

                    for (int k = 0; k < modelAnimation.NodeAnimations.Length; k++)
                    {
                        ref readonly ModelLoader.NodeAnimation nodeAnimation = ref modelAnimation.NodeAnimations[k];
                        if (!(animationTime >= nodeAnimation.Start && animationTime <= nodeAnimation.End))
                        {
                            continue;
                        }

                        int index = Algorithms.BinarySearchLowerBound(nodeAnimation.KeyFramesStart, animationTime, MyComparer.LessThan);
                        index = Math.Max(index, 1);

                        float prevT = nodeAnimation.KeyFramesStart[index - 1];
                        float nextT = nodeAnimation.KeyFramesStart[index];
                        float alpha = 0.0f;

                        if (nodeAnimation.Mode == ModelLoader.NodeAnimation.InterpolationMode.Step)
                        {
                            alpha = 0.0f;
                        }
                        else if (nodeAnimation.Mode == ModelLoader.NodeAnimation.InterpolationMode.Linear)
                        {
                            alpha = MyMath.MapToZeroOne(animationTime, prevT, nextT);
                        }

                        Transformation newTransformation = nodeAnimation.TargetNode.LocalTransform;

                        if (nodeAnimation.Type == ModelLoader.NodeAnimation.AnimationType.Scale ||
                            nodeAnimation.Type == ModelLoader.NodeAnimation.AnimationType.Translation)
                        {
                            Span<Vector3> keyFrames = nodeAnimation.GetKeyFrameDataAsVec3();
                            ref readonly Vector3 prev = ref keyFrames[index - 1];
                            ref readonly Vector3 next = ref keyFrames[index];

                            Vector3 result = Vector3.Lerp(prev, next, alpha);
                            if (nodeAnimation.Type == ModelLoader.NodeAnimation.AnimationType.Scale)
                            {
                                newTransformation.Scale = result;
                            }
                            else if (nodeAnimation.Type == ModelLoader.NodeAnimation.AnimationType.Translation)
                            {
                                newTransformation.Translation = result;
                            }
                        }
                        else if (nodeAnimation.Type == ModelLoader.NodeAnimation.AnimationType.Rotation)
                        {
                            Span<Quaternion> quaternions = nodeAnimation.GetKeyFrameDataAsQuaternion();
                            ref readonly Quaternion prev = ref quaternions[index - 1];
                            ref readonly Quaternion next = ref quaternions[index];

                            Quaternion result = Quaternion.Slerp(prev, next, alpha);
                            newTransformation.Rotation = result;
                        }

                        nodeAnimation.TargetNode.LocalTransform = newTransformation;
                    }
                }
            }
        }

        private void UpdateBuffers(
            ReadOnlySpan<Vector3> vertexPositions,
            ReadOnlySpan<GpuMeshlet> meshlets,
            ReadOnlySpan<GpuMeshletInfo> meshletsInfo,
            ReadOnlySpan<uint> meshletsVertexIndices,
            ReadOnlySpan<byte> meshletsPrimitiveIndices,
            ReadOnlySpan<GpuUnskinnedVertex> unskinnedVertices)
        {
            BBG.Buffer.Recreate(ref drawCommandBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, DrawCommands);
            BBG.Buffer.Recreate(ref meshesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Meshes);
            BBG.Buffer.Recreate(ref meshInstanceBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances);
            BBG.Buffer.Recreate(ref materialsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, GpuMaterials);
            BBG.Buffer.Recreate(ref vertexBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Vertices);
            BBG.Buffer.Recreate(ref vertexPositionsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, vertexPositions);
            BBG.Buffer.Recreate(ref vertexPositionsHostBuffer, BBG.Buffer.MemLocation.HostLocal, BBG.Buffer.MemAccess.MappedCoherent, vertexPositions);
            VertexPositions = new NativeMemoryView<Vector3>(vertexPositionsHostBuffer.Memory, vertexPositionsHostBuffer.NumElements);
            BBG.Buffer.Recreate(ref vertexIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, VertexIndices);
            BBG.Buffer.Recreate(ref meshletBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshlets);
            BBG.Buffer.Recreate(ref meshletInfoBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsInfo);
            BBG.Buffer.Recreate(ref meshletsVertexIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsVertexIndices);
            BBG.Buffer.Recreate(ref meshletsPrimitiveIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsPrimitiveIndices);
            BBG.Buffer.Recreate(ref jointMatricesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, JointMatrices);
            BBG.Buffer.Recreate(ref unskinnedVerticesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, unskinnedVertices);
            BBG.Buffer.Recreate(ref visibleMeshInstanceIdBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances.Length * 6);
            BBG.Buffer.Recreate(ref meshletTasksCmdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances.Length * 6);
            BBG.Buffer.Recreate(ref prevVertexPositionsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, vertexPositions);
        }

        private void UpdateBuffers()
        {
            BBG.Buffer.Recreate(ref drawCommandBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, DrawCommands);
            BBG.Buffer.Recreate(ref meshesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Meshes);
            BBG.Buffer.Recreate(ref meshInstanceBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances);
            BBG.Buffer.Recreate(ref materialsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, GpuMaterials);
            BBG.Buffer.Recreate(ref vertexBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Vertices);
            VertexPositions = new NativeMemoryView<Vector3>(vertexPositionsHostBuffer.Memory, vertexPositionsHostBuffer.NumElements);
            BBG.Buffer.Recreate(ref vertexIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, VertexIndices);
            BBG.Buffer.Recreate(ref jointMatricesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, JointMatrices);
            BBG.Buffer.Recreate(ref visibleMeshInstanceIdBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances.Length * 6);
            BBG.Buffer.Recreate(ref meshletTasksCmdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances.Length * 6);
        }

        private SkinningCmd[] GetSkinningCommands(out int numJoints, ReadOnlySpan<Vector3> vertexPositions)
        {
            numJoints = 0;
            int unskinnedVerticesCount = 0;
            List<SkinningCmd> skinningCmds = new List<SkinningCmd>();
            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref readonly CpuModel model = ref CpuModels[i];
                for (int j = 0; j < model.Nodes.Length; j++)
                {
                    ModelLoader.Node node = model.Nodes[j];
                    if (node.HasSkin)
                    {
                        SkinningCmd skinningCmd = new SkinningCmd();
                        skinningCmd.VertexOffset = unskinnedVerticesCount;
                        skinningCmd.SkinnedNode = node;
                        skinningCmd.JointMatrixOffset = numJoints;

                        skinningCmds.Add(skinningCmd);
                        Range meshRange = GetNodeMeshRange(node, meshInstances);

                        int vertexCount = GetMeshesVertexCount(DrawCommands, vertexPositions, meshRange.Start, meshRange.Count);
                        unskinnedVerticesCount += vertexCount;

                        numJoints += node.Skin.Joints.Length;
                    }
                }
            }

            return skinningCmds.ToArray();
        }

        public void Dispose()
        {
            drawCommandBuffer.Dispose();
            meshesBuffer.Dispose();
            meshInstanceBuffer.Dispose();
            visibleMeshInstanceIdBuffer.Dispose();
            materialsBuffer.Dispose();
            vertexBuffer.Dispose();
            vertexIndicesBuffer.Dispose();
            meshletTasksCmdsBuffer.Dispose();
            meshletTasksCountBuffer.Dispose();
            meshletBuffer.Dispose();
            meshletInfoBuffer.Dispose();
            vertexPositionsBuffer.Dispose();
            meshletsVertexIndicesBuffer.Dispose();
            meshletsPrimitiveIndicesBuffer.Dispose();
            jointMatricesBuffer.Dispose();
            vertexPositionsHostBuffer.Dispose();
            unskinnedVerticesBuffer.Dispose();
            prevVertexPositionsBuffer.Dispose();

            BVH.Dispose();

            skinningShaderProgram.Dispose();

            OpaqueMeshInstanceIdBuffer.Dispose();
            TransparentMeshInstanceIdBuffer.Dispose();
        }

        private static GpuUnskinnedVertex[] LoadUnskinnedVertices(in ModelLoader.Model model)
        {
            List<GpuUnskinnedVertex> unskinnedVertices = new List<GpuUnskinnedVertex>();
            int unskinnedVertexOffset = 0;
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                ModelLoader.Node node = model.Nodes[i];
                if (node.HasSkin)
                {
                    Range meshRange = GetNodeMeshRange(node, model.GpuModel.MeshInstances);
                    int vertexOffset = model.GpuModel.DrawCommands[meshRange.Start].BaseVertex;
                    int vertexCount = GetMeshesVertexCount(model.GpuModel.DrawCommands, model.GpuModel.VertexPositions, meshRange.Start, meshRange.Count);

                    ReadOnlySpan<Vector3> vertexPositions = new ReadOnlySpan<Vector3>(model.GpuModel.VertexPositions, vertexOffset, vertexCount);
                    ReadOnlySpan<GpuVertex> vertices = new ReadOnlySpan<GpuVertex>(model.GpuModel.Vertices, vertexOffset, vertexCount);
                    ReadOnlySpan<Vector4i> jointIndices = new ReadOnlySpan<Vector4i>(model.GpuModel.JointIndices, unskinnedVertexOffset, vertexCount);
                    ReadOnlySpan<Vector4> jointWeights = new ReadOnlySpan<Vector4>(model.GpuModel.JointWeights, unskinnedVertexOffset, vertexCount);

                    for (int j = 0; j < vertexPositions.Length; j++)
                    {
                        ref readonly GpuVertex vertex = ref vertices[j];
                        ref readonly Vector3 vertexPos = ref vertexPositions[j];

                        GpuUnskinnedVertex unskinnedVertex = new GpuUnskinnedVertex();
                        unskinnedVertex.JointIndices = jointIndices[j];
                        unskinnedVertex.JointWeights = jointWeights[j];
                        unskinnedVertex.Position = vertexPos;
                        unskinnedVertex.Tangent = vertex.Tangent;
                        unskinnedVertex.Normal = vertex.Normal;

                        unskinnedVertices.Add(unskinnedVertex);
                    }
                    unskinnedVertexOffset += vertexCount;
                }
            }

            return unskinnedVertices.ToArray();
        }

        private static bool IsMaterialReferenced(int materialId, ReadOnlySpan<GpuMesh> meshes, Range meshExcludeRange)
        {
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshExcludeRange.Contains(i))
                {
                    continue;
                }

                if (meshes[i].MaterialId == materialId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsTextureHandleReferenced(BBG.Texture.BindlessHandle bindlessHandle, Span<GpuMaterial> materials, SortedSet<int> ignoreMaterials)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (ignoreMaterials.Contains(i))
                {
                    continue;
                }

                ref GpuMaterial material = ref materials[i];

                for (int j = 0; j < GpuMaterial.TEXTURE_COUNT; j++)
                {
                    GpuMaterial.TextureType textureType = (GpuMaterial.TextureType)j;
                    if (material[textureType] == bindlessHandle)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static CpuModel ConvertToCpuModel(in ModelLoader.Model model)
        {
            CpuModel newCpuModel = new CpuModel();

            newCpuModel.Nodes = new ModelLoader.Node[model.Nodes.Length];
            model.RootNode.DeepClone(newCpuModel.Nodes, model.Nodes);

            newCpuModel.Animations = new ModelLoader.ModelAnimation[model.Animations.Length];
            for (int i = 0; i < newCpuModel.Animations.Length; i++)
            {
                ref readonly ModelLoader.ModelAnimation oldAnimation = ref model.Animations[i];
                newCpuModel.Animations[i] = oldAnimation.DeepClone(newCpuModel.Nodes);
            }

            newCpuModel.EnabledAnimations = new BitArray(newCpuModel.Animations.Length, true);

            return newCpuModel;
        }

        private static Range GetNodeMeshRange(ModelLoader.Node node, ReadOnlySpan<GpuMeshInstance> meshInstances)
        {
            if (!node.HasMeshInstances)
            {
                throw new ArgumentException($"Node has no mesh instances");
            }

            Range meshRange = GetInstancesMeshRange(node.MeshInstanceRange, meshInstances);
            return meshRange;
        }

        public static Range GetInstancesMeshRange(Range instanceRange, ReadOnlySpan<GpuMeshInstance> meshInstances)
        {
            // Assumes that for mesh instances GpuMeshInstance[i + 1].MeshIndex >= GpuMeshInstance[i].MeshIndex

            Range range = new Range();
            range.Start = meshInstances[instanceRange.Start].MeshId;
            range.End = meshInstances[instanceRange.End - 1].MeshId + 1;

            return range;
        }

        private static int GetMeshesVertexCount(ReadOnlySpan<BBG.DrawElementsIndirectCommand> drawCmds, ReadOnlySpan<Vector3> vertexPositions, int startMesh, int count = 1)
        {
            int baseVertex = drawCmds[startMesh].BaseVertex;
            int nextBaseVertex = startMesh + count == drawCmds.Length ? vertexPositions.Length : drawCmds[startMesh + count].BaseVertex;
            return nextBaseVertex - baseVertex;
        }
    }
}
