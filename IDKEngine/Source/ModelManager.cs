using System;
using System.Linq;
using System.Threading;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Bvh;
using IDKEngine.Utils;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class ModelManager : IDisposable
    {
        public record struct CpuModel
        {
            public ModelLoader.Node Root => Nodes[0];
            public ModelLoader.Node[] Nodes;

            public ModelLoader.ModelAnimation[] Animations;
        }

        private record struct SkinningCmd
        {
            /// <summary>
            /// The node with a skin (containing meshes)
            /// </summary>
            public ModelLoader.Node SkinnedNode;

            /// <summary>
            /// The offset into the buffer containing animated vertices for the first mesh from <see cref="SkinnedNode"/>
            /// </summary>
            public int VertexOffset;
        }

        public CpuModel[] CpuModels = [];
        public BVH BVH;

        public BBG.DrawElementsIndirectCommand[] DrawCommands = [];
        private BBG.TypedBuffer<BBG.DrawElementsIndirectCommand> drawCommandBuffer;

        public GpuMesh[] Meshes = [];
        private BBG.TypedBuffer<GpuMesh> meshesBuffer;

        public ReadOnlyArray<GpuMeshInstance> MeshInstances => new ReadOnlyArray<GpuMeshInstance>(meshInstances);
        private GpuMeshInstance[] meshInstances = [];
        private BBG.TypedBuffer<GpuMeshInstance> meshInstanceBuffer;
        private BitArray meshInstancesDirty;

        public GpuMaterial[] Materials = [];
        private BBG.TypedBuffer<GpuMaterial> materialsBuffer;

        public GpuVertex[] Vertices = [];
        private BBG.TypedBuffer<GpuVertex> vertexBuffer;

        public Vector3[] VertexPositions = [];
        private BBG.TypedBuffer<Vector3> vertexPositionsBuffer;

        public uint[] VertexIndices = [];
        private BBG.TypedBuffer<uint> vertexIndicesBuffer;

        public Matrix3x4[] JointMatrices = [];
        private BBG.TypedBuffer<Matrix3x4> jointMatricesBuffer;

        private BBG.TypedBuffer<Vector3> prevVertexPositionsBuffer;
        private BBG.TypedBuffer<uint> visibleMeshInstancesBuffer;

        private BBG.TypedBuffer<GpuMeshlet> meshletBuffer;
        private BBG.TypedBuffer<GpuMeshletInfo> meshletInfoBuffer;
        private BBG.TypedBuffer<uint> meshletsVertexIndicesBuffer;
        private BBG.TypedBuffer<byte> meshletsPrimitiveIndicesBuffer;
        private BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV> meshletTasksCmdsBuffer;
        private BBG.TypedBuffer<int> meshletTasksCountBuffer;

        private BBG.TypedBuffer<Vector4i> jointIndicesBuffer;
        private BBG.TypedBuffer<Vector4> jointWeightsBuffer;
        private BBG.TypedBuffer<Vector3> skinnedVertexPositionsHostBuffer;
        private BBG.TypedBuffer<GpuUnskinnedVertex> unskinnedVerticesBuffer;

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
            visibleMeshInstancesBuffer = new BBG.TypedBuffer<uint>();
            materialsBuffer = new BBG.TypedBuffer<GpuMaterial>();
            vertexBuffer = new BBG.TypedBuffer<GpuVertex>();
            vertexPositionsBuffer = new BBG.TypedBuffer<Vector3>();
            vertexIndicesBuffer = new BBG.TypedBuffer<uint>();
            meshletTasksCmdsBuffer = new BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV>();
            meshletTasksCountBuffer = new BBG.TypedBuffer<int>();
            meshletBuffer = new BBG.TypedBuffer<GpuMeshlet>();
            meshletInfoBuffer = new BBG.TypedBuffer<GpuMeshletInfo>();
            meshletsVertexIndicesBuffer = new BBG.TypedBuffer<uint>();
            meshletsPrimitiveIndicesBuffer = new BBG.TypedBuffer<byte>();
            jointIndicesBuffer = new BBG.TypedBuffer<Vector4i>();
            jointWeightsBuffer = new BBG.TypedBuffer<Vector4>();
            jointMatricesBuffer = new BBG.TypedBuffer<Matrix3x4>();
            unskinnedVerticesBuffer = new BBG.TypedBuffer<GpuUnskinnedVertex>();
            skinnedVertexPositionsHostBuffer = new BBG.TypedBuffer<Vector3>();
            prevVertexPositionsBuffer = new BBG.TypedBuffer<Vector3>();

            drawCommandBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 0);
            meshesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 1);
            meshInstanceBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 2);
            visibleMeshInstancesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 3);
            materialsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 4);
            vertexBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 5);
            vertexPositionsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 6);
            meshletTasksCmdsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 7);
            meshletTasksCountBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 8);
            meshletBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 9);
            meshletInfoBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 10);
            meshletsVertexIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 11);
            meshletsPrimitiveIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 12);
            jointIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 13);
            jointWeightsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 14);
            jointMatricesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 15);
            unskinnedVerticesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 16);
            prevVertexPositionsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 17);

            BVH = new BVH();

            skinningShaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "Skinning/compute.glsl"));
        }

        // TODO: Change to ReadOnlySpan in .NET 9
        public unsafe void Add(params ModelLoader.Model?[] models)
        {
            // This data is not stored on the CPU so retrieve it
            meshletBuffer.DownloadElements(out GpuMeshlet[] meshlets);
            meshletInfoBuffer.DownloadElements(out GpuMeshletInfo[] meshletsInfo);
            meshletsVertexIndicesBuffer.DownloadElements(out uint[] meshletsVertexIndices);
            meshletsPrimitiveIndicesBuffer.DownloadElements(out byte[] meshletsPrimitiveIndices);
            jointIndicesBuffer.DownloadElements(out Vector4i[] jointIndices);
            jointWeightsBuffer.DownloadElements(out Vector4[] jointWeights);
            unskinnedVerticesBuffer.DownloadElements(out GpuUnskinnedVertex[] unskinnedVertices);

            int prevDrawCommandsLength = DrawCommands.Length;
            for (int i = 0; i < models.Length; i++)
            {
                if (!models[i].HasValue)
                {
                    continue;
                }

                ModelLoader.Model model = models[i].Value;
                ref readonly ModelLoader.GpuModel gpuModel = ref model.GpuModel;
                CpuModel myModel = ConvertToCpuModel(model);

                Helper.ArrayAdd(ref unskinnedVertices, GetUnskinnedVertices(model));

                // The following data needs to be readjusted when deleting models as it has offsets backed in

                Helper.ArrayAdd(ref jointIndices, gpuModel.JointIndices);
                for (int j = jointIndices.Length - gpuModel.JointIndices.Length; j < jointIndices.Length; j++)
                {
                    jointIndices[j] += new Vector4i(JointMatrices.Length);
                }

                for (int j = 0; j < myModel.Nodes.Length; j++)
                {
                    ModelLoader.Node node = myModel.Nodes[j];
                    if (node.HasMeshInstances)
                    {
                        node.MeshInstanceIds.Start += meshInstances.Length;
                    }
                }
                Helper.ArrayAdd(ref CpuModels, [myModel]);

                Helper.ArrayAdd(ref DrawCommands, gpuModel.DrawCommands);
                for (int j = DrawCommands.Length - gpuModel.DrawCommands.Length; j < DrawCommands.Length; j++)
                {
                    ref BBG.DrawElementsIndirectCommand newDrawCmd = ref DrawCommands[j];
                    newDrawCmd.BaseInstance += (uint)meshInstances.Length;
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
                    newMesh.MaterialId += Materials.Length;
                    newMesh.MeshletsStart += meshlets.Length;
                }

                Helper.ArrayAdd(ref meshlets, gpuModel.Meshlets);
                for (int j = meshlets.Length - gpuModel.Meshlets.Length; j < meshlets.Length; j++)
                {
                    ref GpuMeshlet newMeshlet = ref meshlets[j];
                    newMeshlet.VertexOffset += (uint)meshletsVertexIndices.Length;
                    newMeshlet.IndicesOffset += (uint)meshletsPrimitiveIndices.Length;
                }

                Helper.ArrayAdd(ref Materials, gpuModel.Materials);
                Helper.ArrayAdd(ref Vertices, gpuModel.Vertices);
                Helper.ArrayAdd(ref VertexPositions, gpuModel.VertexPositions);
                Helper.ArrayAdd(ref VertexIndices, gpuModel.VertexIndices);
                Helper.ArrayAdd(ref meshletsInfo, gpuModel.MeshletsInfo);
                Helper.ArrayAdd(ref meshletsVertexIndices, gpuModel.MeshletsVertexIndices);
                Helper.ArrayAdd(ref meshletsPrimitiveIndices, gpuModel.MeshletsLocalIndices);
                Helper.ArrayAdd(ref jointWeights, gpuModel.JointWeights);
                Array.Resize(ref JointMatrices, JointMatrices.Length + model.GetNumJoints());
            }

            {
                ReadOnlySpan<BBG.DrawElementsIndirectCommand> newDrawCommands = new ReadOnlySpan<BBG.DrawElementsIndirectCommand>(DrawCommands, prevDrawCommandsLength, DrawCommands.Length - prevDrawCommandsLength);
                BVH.GeometryDesc[] geometriesDesc = new BVH.GeometryDesc[newDrawCommands.Length];
                for (int i = 0; i < geometriesDesc.Length; i++)
                {
                    ref readonly BBG.DrawElementsIndirectCommand cmd = ref newDrawCommands[i];

                    BVH.GeometryDesc geometryDesc = new BVH.GeometryDesc();
                    geometryDesc.TriangleCount = cmd.IndexCount / 3;
                    geometryDesc.TriangleOffset = cmd.FirstIndex / 3;
                    geometryDesc.BaseVertex = cmd.BaseVertex;
                    geometryDesc.VertexOffset = cmd.FirstIndex;

                    geometriesDesc[i] = geometryDesc;
                }

                BVH.AddMeshes(geometriesDesc, VertexPositions, VertexIndices, meshInstances);
                for (int i = 0; i < DrawCommands.Length; i++)
                {
                    Meshes[i].BlasRootNodeOffset = BVH.BlasesDesc[i].RootNodeOffset;
                }
            }

            skinningCmds = GetSkinningCommands();
            meshInstancesDirty = new BitArray(meshInstances.Length, true);

            BBG.Buffer.Recreate(ref drawCommandBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, DrawCommands);
            BBG.Buffer.Recreate(ref meshesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Meshes);
            BBG.Buffer.Recreate(ref meshInstanceBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances);
            BBG.Buffer.Recreate(ref materialsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Materials);
            BBG.Buffer.Recreate(ref vertexBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Vertices);
            BBG.Buffer.Recreate(ref vertexPositionsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, VertexPositions);
            BBG.Buffer.Recreate(ref vertexIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, VertexIndices);
            BBG.Buffer.Recreate(ref meshletBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshlets);
            BBG.Buffer.Recreate(ref meshletInfoBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsInfo);
            BBG.Buffer.Recreate(ref meshletsVertexIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsVertexIndices);
            BBG.Buffer.Recreate(ref meshletsPrimitiveIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsPrimitiveIndices);
            BBG.Buffer.Recreate(ref jointIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, jointIndices);
            BBG.Buffer.Recreate(ref jointWeightsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, jointWeights);
            BBG.Buffer.Recreate(ref jointMatricesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, JointMatrices);
            BBG.Buffer.Recreate(ref skinnedVertexPositionsHostBuffer, BBG.Buffer.MemLocation.HostLocal, BBG.Buffer.MemAccess.MappedCoherent, unskinnedVertices.Length);
            BBG.Buffer.Recreate(ref unskinnedVerticesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, unskinnedVertices);
            BBG.Buffer.Recreate(ref visibleMeshInstancesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances.Length * 6);
            BBG.Buffer.Recreate(ref meshletTasksCmdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshInstances.Length * 6);
            BBG.Buffer.Recreate(ref meshletTasksCountBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);
            BBG.Buffer.Recreate(ref prevVertexPositionsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, VertexPositions);
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

        public void Update(float animationsTime, out bool anyAnimatedNodeMoved, out bool anyMeshInstanceMoved)
        {
            UpdateNodeAnimations(animationsTime, out anyAnimatedNodeMoved);
            UpdateNodeHierarchy();
            UpdateMeshInstanceBufferBatched(out anyMeshInstanceMoved);

            ComputeSkinnedPositions(anyAnimatedNodeMoved);

            // Only need to update BLAS when node was animated, could use more granular check
            if (anyAnimatedNodeMoved)
            {
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    ref readonly SkinningCmd cmd = ref skinningCmds[i];
                    Range meshRange = GetNodeMeshRange(cmd.SkinnedNode);
                    BVH.GpuBlasesRefit(meshRange.Start, meshRange.Count);
                }
            }

            // Only need to update TLAS if a BLAS was moved or animated
            if (anyMeshInstanceMoved || anyAnimatedNodeMoved)
            {
                BVH.TlasBuild();
            }
        }

        public unsafe void ComputeSkinnedPositions(bool anyAnimatedNodeMoved)
        {
            if (anyAnimatedNodeMoved)
            {
                int jointsProcessed = 0;
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    ref readonly SkinningCmd skinningCmd = ref skinningCmds[i];
                    ref readonly ModelLoader.Skin skin = ref skinningCmd.SkinnedNode.Skin;

                    Matrix4 inverseNodeTransform = Matrix4.Invert(skinningCmd.SkinnedNode.GlobalTransform);
                    for (int j = 0; j < skin.Joints.Length; j++)
                    {
                        JointMatrices[jointsProcessed++] = MyMath.Matrix4x4ToTranposed3x4(skin.InverseJointMatrices[j] * skin.Joints[j].GlobalTransform * inverseNodeTransform);
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
                            fixed (Vector3* ptrAllVertexPositions = VertexPositions)
                            {
                                Memory.CopyElements(
                                    src: &skinnedVertexPositionsHostBuffer.Memory[skinningCmd.VertexOffset + localSkinningVertexOffset],
                                    dest: &ptrAllVertexPositions[baseVertex],
                                    numElements: vertexCount
                                );
                            }

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
                        skinningShaderProgram.Upload(2, (uint)vertexCount);

                        BBG.Cmd.UseShaderProgram(skinningShaderProgram);
                        BBG.Computing.Dispatch((vertexCount + 64 - 1) / 64, 1, 1);
                    });
                    vertexPositionsBuffer.CopyElementsTo(skinnedVertexPositionsHostBuffer, outputVertexOffset, skinningCmd.VertexOffset, vertexCount);
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
            meshletTasksCountBuffer.UploadElements(count);
        }

        public int GetMeshesVertexCount(int startMesh, int count = 1)
        {
            return GetMeshesVertexCount(DrawCommands, VertexPositions, startMesh, count);
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

        public Range GetNodeMeshRange(ModelLoader.Node node)
        {
            return GetNodeMeshRange(node, meshInstances);
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
                            meshInstancesDirty[i] = true;
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

        private void UpdateNodeHierarchy()
        {
            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref readonly CpuModel cpuModel = ref CpuModels[i];
                ModelLoader.Node.TraverseUpdate(cpuModel.Root, (ModelLoader.Node node) =>
                {
                    Transformation nodeTransformBefore = Transformation.FromMatrix(node.GlobalTransform);
                    node.UpdateGlobalTransform();

                    if (node.HasMeshInstances)
                    {
                        Transformation nodeTransformAfter = Transformation.FromMatrix(node.GlobalTransform);
                        for (int j = node.MeshInstanceIds.Start; j < node.MeshInstanceIds.End; j++)
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
        }

        private void UpdateNodeAnimations(float time, out bool anyAnimatedNodeMoved)
        {
            anyAnimatedNodeMoved = false;
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
                    ref readonly ModelLoader.ModelAnimation modelAnimation = ref cpuModel.Animations[j];
                    float animationTime = time % modelAnimation.Duration;

                    for (int k = 0; k < modelAnimation.NodeAnimations.Length; k++)
                    {
                        ref readonly ModelLoader.NodeAnimation nodeAnimation = ref modelAnimation.NodeAnimations[k];
                        if (!(animationTime >= nodeAnimation.Start && animationTime <= nodeAnimation.End))
                        {
                            continue;
                        }

                        int index = Algorithms.BinarySearchLowerBound(nodeAnimation.KeyFramesStart, animationTime, Comparer<float>.Default.Compare);
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
                            if (nodeAnimation.Type == ModelLoader.NodeAnimation.AnimationType.Translation)
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

                        anyAnimatedNodeMoved = true;
                    }
                }
            }
        }

        private SkinningCmd[] GetSkinningCommands()
        {
            int totalSkinningCmds = 0;
            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref readonly CpuModel cpuModel = ref CpuModels[i];
                for (int j = 0; j < cpuModel.Nodes.Length; j++)
                {
                    ModelLoader.Node node = cpuModel.Nodes[j];
                    if (node.HasSkin)
                    {
                        totalSkinningCmds++;
                    }
                }
            }

            SkinningCmd[] skinningCmds = new SkinningCmd[totalSkinningCmds];
            int cmdsCount = 0;
            int unskinnedVerticesCount = 0;
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

                        skinningCmds[cmdsCount++] = skinningCmd;
                        Range meshRange = GetNodeMeshRange(node, meshInstances);

                        int vertexCount = GetMeshesVertexCount(meshRange.Start, meshRange.Count);
                        unskinnedVerticesCount += vertexCount;
                    }
                }
            }

            return skinningCmds;
        }

        public void Dispose()
        {
            drawCommandBuffer.Dispose();
            meshesBuffer.Dispose();
            meshInstanceBuffer.Dispose();
            visibleMeshInstancesBuffer.Dispose();
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
            jointIndicesBuffer.Dispose();
            jointWeightsBuffer.Dispose();
            jointMatricesBuffer.Dispose();
            skinnedVertexPositionsHostBuffer.Dispose();
            unskinnedVerticesBuffer.Dispose();
            prevVertexPositionsBuffer.Dispose();

            BVH.Dispose();

            skinningShaderProgram.Dispose();
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

            return newCpuModel;
        }

        private static GpuUnskinnedVertex[] GetUnskinnedVertices(in ModelLoader.Model model)
        {
            int totalUnskinnedVertices = 0;
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                ModelLoader.Node node = model.Nodes[i];
                if (node.HasSkin)
                {
                    Range meshRange = GetNodeMeshRange(node, model.GpuModel.MeshInstances);
                    int vertexCount = GetMeshesVertexCount(model.GpuModel.DrawCommands, model.GpuModel.VertexPositions, meshRange.Start, meshRange.Count);
                    totalUnskinnedVertices += vertexCount;
                }
            }
            
            GpuUnskinnedVertex[] unskinnedVertices = new GpuUnskinnedVertex[totalUnskinnedVertices];
            int unskinnedVerticesCount = 0;
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                ModelLoader.Node node = model.Nodes[i];
                if (node.HasSkin)
                {
                    Range meshRange = GetNodeMeshRange(node, model.GpuModel.MeshInstances);
                    int startVertex = model.GpuModel.DrawCommands[node.MeshInstanceIds.Start].BaseVertex;
                    int vertexCount = GetMeshesVertexCount(model.GpuModel.DrawCommands, model.GpuModel.VertexPositions, meshRange.Start, meshRange.Count);

                    ReadOnlySpan<Vector3> vertexPositions = new ReadOnlySpan<Vector3>(model.GpuModel.VertexPositions, startVertex, vertexCount);
                    ReadOnlySpan<GpuVertex> vertices = new ReadOnlySpan<GpuVertex>(model.GpuModel.Vertices, startVertex, vertexCount);
                    
                    for (int j = 0; j < vertexPositions.Length; j++)
                    {
                        ref readonly GpuVertex vertex = ref vertices[j];
                        ref readonly Vector3 vertexPos = ref vertexPositions[j];

                        GpuUnskinnedVertex unskinnedVertex = new GpuUnskinnedVertex();
                        unskinnedVertex.Position = vertexPos;
                        unskinnedVertex.Tangent = vertex.Tangent;
                        unskinnedVertex.Normal = vertex.Normal;

                        unskinnedVertices[unskinnedVerticesCount++] = unskinnedVertex;
                    }
                }
            }

            return unskinnedVertices;
        }

        private static Range GetNodeMeshRange(ModelLoader.Node node, ReadOnlySpan<GpuMeshInstance> meshInstances)
        {
            if (!node.HasMeshInstances)
            {
                return new Range(0, 0);
            }

            ref readonly GpuMeshInstance first = ref meshInstances[node.MeshInstanceIds.Start];
            ref readonly GpuMeshInstance last = ref meshInstances[node.MeshInstanceIds.End - 1];
            return new Range(first.MeshId, (last.MeshId - first.MeshId) + 1);
        }

        private static int GetMeshesVertexCount(ReadOnlySpan<BBG.DrawElementsIndirectCommand> drawCmds, ReadOnlySpan<Vector3> vertexPositions, int startMesh, int count = 1)
        {
            int baseVertex = drawCmds[startMesh].BaseVertex;
            int nextBaseVertex = startMesh + count == drawCmds.Length ? vertexPositions.Length : drawCmds[startMesh + count].BaseVertex;
            return nextBaseVertex - baseVertex;
        }
    }
}
