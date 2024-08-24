using System;
using System.Linq;
using System.Diagnostics;
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
            public bool IsSkinned;

            public ModelLoader.Animation[] Animations;
        }

        public bool RunAnimations
        {
            get
            {
                return globalAnimationsTimer.IsRunning;
            }

            set
            {
                if (value)
                {
                    runSkinningShader = true;
                    globalAnimationsTimer.Start();
                }
                else
                {
                    globalAnimationsTimer.Stop();
                }
            }
        }

        private record struct SkinningCmd
        {
            public int InputVertexOffset;
            public int OutputVertexOffset;
            public int VertexCount;
        }

        public CpuModel[] CpuModels = Array.Empty<CpuModel>();
        public BVH BVH;

        public BBG.DrawElementsIndirectCommand[] DrawCommands = Array.Empty<BBG.DrawElementsIndirectCommand>();
        private BBG.TypedBuffer<BBG.DrawElementsIndirectCommand> drawCommandBuffer;

        public GpuMesh[] Meshes = Array.Empty<GpuMesh>();
        private BBG.TypedBuffer<GpuMesh> meshesBuffer;

        public ReadOnlyArray<GpuMeshInstance> MeshInstances => new ReadOnlyArray<GpuMeshInstance>(meshInstances);
        private GpuMeshInstance[] meshInstances = Array.Empty<GpuMeshInstance>();
        private BBG.TypedBuffer<GpuMeshInstance> meshInstanceBuffer;
        private BitArray meshInstancesDirty;

        public GpuMaterial[] Materials = Array.Empty<GpuMaterial>();
        private BBG.TypedBuffer<GpuMaterial> materialsBuffer;

        public GpuVertex[] Vertices = Array.Empty<GpuVertex>();
        private BBG.TypedBuffer<GpuVertex> vertexBuffer;

        public Vector3[] VertexPositions = Array.Empty<Vector3>();
        private BBG.TypedBuffer<Vector3> vertexPositionsBuffer;

        public uint[] VertexIndices = Array.Empty<uint>();
        private BBG.TypedBuffer<uint> vertexIndicesBuffer;

        public Matrix3x4[] JointMatrices = Array.Empty<Matrix3x4>();
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

        private readonly Stopwatch globalAnimationsTimer = new Stopwatch();
        private bool runSkinningShader;
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
            RunAnimations = true;
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

                // The following data needs to be readjusted when deleting models as it has offsets backed in

                Helper.ArrayAdd(ref jointIndices, gpuModel.JointIndices);
                for (int j = jointIndices.Length - gpuModel.JointIndices.Length; j < jointIndices.Length; j++)
                {
                    jointIndices[j] += new Vector4i(JointMatrices.Length);
                }

                CpuModel myModel = GetCpuModel(model);
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
                    newMesh.MaterialIndex += Materials.Length;
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
                
                if (myModel.IsSkinned)
                {
                    Helper.ArrayAdd(ref unskinnedVertices, GetUnskinnedVertices(gpuModel.VertexPositions, gpuModel.Vertices));
                }
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

        public void Update(out bool anyAnimatedNodeMoved, out bool anyMeshInstanceMoved)
        {
            UpdateNodeAnimations(out anyAnimatedNodeMoved);
            UpdateNodeHierarchy();
            UpdateMeshInstanceBufferBatched(out anyMeshInstanceMoved);

            ComputeSkinnedPositions();

            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref CpuModel model = ref CpuModels[i];
                if (model.IsSkinned)
                {
                    Range range = CpuModelGetMeshRange(model);
                    BVH.BlasesRefit(range.Start, range.Count);
                }
            }

            BVH.TlasBuild();
        }

        public unsafe void ComputeSkinnedPositions()
        {
            if (skinningCmds.Length == 0)
            {
                return;
            }

            if (RunAnimations)
            {
                int jointsProcessed = 0;
                for (int i = 0; i < CpuModels.Length; i++)
                {
                    ref readonly CpuModel cpuModel = ref CpuModels[i];
                    if (!cpuModel.IsSkinned)
                    {
                        continue;
                    }

                    for (int j = 0; j < cpuModel.Nodes.Length; j++)
                    {
                        ModelLoader.Node node = cpuModel.Nodes[j];
                        if (node.HasSkin)
                        {
                            Matrix4 inverseNodeTransform = Matrix4.Invert(node.GlobalTransform);

                            ref readonly ModelLoader.Skin skin = ref node.Skin;
                            for (int k = 0; k < skin.Joints.Length; k++)
                            {
                                JointMatrices[jointsProcessed++] = MyMath.Matrix4x4ToTranposed3x4(skin.InverseJointMatrices[k] * skin.Joints[k].GlobalTransform * inverseNodeTransform);
                            }
                        }
                    }
                }
                jointMatricesBuffer.UploadElements(JointMatrices);
            }

            if (fenceCopiedSkinnedVerticesToHost.HasValue && fenceCopiedSkinnedVerticesToHost.Value.TryWait())
            {
                // Read the skinned vertices with one frame delay to avoid sync
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    ref readonly SkinningCmd cmd = ref skinningCmds[i];
                    fixed (Vector3* dest = VertexPositions)
                    {
                        Memory.CopyElements(skinnedVertexPositionsHostBuffer.MappedMemory + cmd.InputVertexOffset, dest + cmd.OutputVertexOffset, cmd.VertexCount);
                    }
                }
                fenceCopiedSkinnedVerticesToHost.Value.Dispose();
                fenceCopiedSkinnedVerticesToHost = null;
            }

            if (runSkinningShader)
            {
                for (int i = 0; i < skinningCmds.Length; i++)
                {
                    SkinningCmd cmd = skinningCmds[i];
                    BBG.Computing.Compute("Compute Skinned vertices", () =>
                    {
                        skinningShaderProgram.Upload(0, (uint)cmd.InputVertexOffset);
                        skinningShaderProgram.Upload(1, (uint)cmd.OutputVertexOffset);
                        skinningShaderProgram.Upload(2, (uint)cmd.VertexCount);

                        BBG.Cmd.UseShaderProgram(skinningShaderProgram);
                        BBG.Computing.Dispatch((cmd.VertexCount + 64 - 1) / 64, 1, 1);
                    });
                    vertexPositionsBuffer.CopyElementsTo(skinnedVertexPositionsHostBuffer, cmd.OutputVertexOffset, cmd.InputVertexOffset, cmd.VertexCount);
                }
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);

                fenceCopiedSkinnedVerticesToHost = new BBG.Fence();

                if (!RunAnimations)
                {
                    // Skinning shader must stop with one frame delay after Animations have been stoped so velocity becomes zero
                    runSkinningShader = false;
                }
            }
        }
        
        public Range CpuModelGetMeshRange(in CpuModel model)
        {
            int min = int.MaxValue;
            int max = int.MinValue;
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                ModelLoader.Node node = model.Nodes[i];
                if (node.HasMeshInstances)
                {
                    ref readonly GpuMeshInstance first = ref meshInstances[node.MeshInstanceIds.Start];
                    ref readonly GpuMeshInstance last = ref meshInstances[node.MeshInstanceIds.End - 1];
                    min = Math.Min(min, first.MeshId);
                    max = Math.Max(max, last.MeshId);
                }
            }

            return new Range(min, (max - min) + 1);
        }

        public void SetMeshInstance(int index, in GpuMeshInstance meshInstance)
        {
            meshInstances[index] = meshInstance;
            meshInstancesDirty[index] = true;
        }

        public void UpdateVertexPositionBuffer(int start, int count)
        {
            vertexPositionsBuffer.UploadElements(start, count, VertexPositions[start]);
        }

        public void UpdateMeshBuffer(int start, int count)
        {
            if (count == 0) return;
            meshesBuffer.UploadElements(start, count, Meshes[start]);
        }

        public void UpdateDrawCommandBuffer(int start, int count)
        {
            if (count == 0) return;
            drawCommandBuffer.UploadElements(start, count, DrawCommands[start]);
        }

        public void UpdateMeshInstanceBuffer(int start, int count)
        {
            meshInstanceBuffer.UploadElements(start, count, meshInstances[start]);
            for (int i = 0; i < count; i++)
            {
                meshInstancesDirty[start + i] = false;
            }
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

        public int GetMeshesVertexCount(int startMesh, int count = 1)
        {
            int baseVertex = DrawCommands[startMesh].BaseVertex;
            int nextBaseVertex = startMesh + count == DrawCommands.Length ? VertexPositions.Length : DrawCommands[startMesh + count].BaseVertex;
            return nextBaseVertex - baseVertex;
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

                    UpdateMeshInstanceBuffer(batchStart, batchEnd - batchStart);
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

                            meshInstance.ModelMatrix = adjustedTransform.Matrix;
                            SetMeshInstance(j, meshInstance);
                        }
                    }
                });
            }
        }
        
        private void UpdateNodeAnimations(out bool anyAnimatedNodeMoved)
        {
            anyAnimatedNodeMoved = false;
            if (!RunAnimations)
            {
                return;
            }

            float globalTime = (float)globalAnimationsTimer.Elapsed.TotalSeconds;
            for (int i = 0; i < CpuModels.Length; i++)
            {
                ref readonly CpuModel cpuModel = ref CpuModels[i];
                for (int j = 0; j < cpuModel.Animations.Length; j++)
                {
                    ref readonly ModelLoader.Animation animation = ref cpuModel.Animations[j];
                    float animationTime = globalTime % animation.Duration;

                    for (int k = 0; k < animation.Samplers.Length; k++)
                    {
                        ref readonly ModelLoader.AnimationSampler sampler = ref animation.Samplers[k];
                        if (!(animationTime >= sampler.Start && animationTime <= sampler.End))
                        {
                            continue;
                        }
                        anyAnimatedNodeMoved = true;

                        int index = Algorithms.BinarySearchLowerBound(sampler.KeyFramesStart, animationTime, Comparer<float>.Default.Compare);
                        index = Math.Max(index, 1);

                        float prevT = sampler.KeyFramesStart[index - 1];
                        float nextT = sampler.KeyFramesStart[index];
                        float alpha = 0.0f;

                        if (sampler.Mode == ModelLoader.AnimationSampler.InterpolationMode.Step)
                        {
                            alpha = 0.0f;
                        }
                        else if (sampler.Mode == ModelLoader.AnimationSampler.InterpolationMode.Linear)
                        {
                            alpha = MyMath.MapToZeroOne(animationTime, prevT, nextT);
                        }

                        Transformation newTransformation = sampler.TargetNode.LocalTransform;

                        if (sampler.Type == ModelLoader.AnimationSampler.AnimationType.Scale ||
                            sampler.Type == ModelLoader.AnimationSampler.AnimationType.Translation)
                        {
                            Span<Vector3> keyFrames = sampler.GetKeyFrameDataAsVec3();
                            ref readonly Vector3 prev = ref keyFrames[index - 1];
                            ref readonly Vector3 next = ref keyFrames[index];

                            Vector3 result = Vector3.Lerp(prev, next, alpha);
                            if (sampler.Type == ModelLoader.AnimationSampler.AnimationType.Scale)
                            {
                                newTransformation.Scale = result;
                            }
                            if (sampler.Type == ModelLoader.AnimationSampler.AnimationType.Translation)
                            {
                                newTransformation.Translation = result;
                            }
                        }
                        else if (sampler.Type == ModelLoader.AnimationSampler.AnimationType.Rotation)
                        {
                            Span<Quaternion> quaternions = sampler.GetKeyFrameDataAsQuaternion();
                            ref readonly Quaternion prev = ref quaternions[index - 1];
                            ref readonly Quaternion next = ref quaternions[index];

                            Quaternion result = Quaternion.Slerp(prev, next, alpha);
                            newTransformation.Rotation = result;
                        }
                        sampler.TargetNode.LocalTransform = newTransformation;
                    }
                }
            }
        }

        private SkinningCmd[] GetSkinningCommands()
        {
            int totalSkinningCmds = CpuModels.Sum(model => model.IsSkinned ? 1 : 0);

            SkinningCmd[] skinningCmds = new SkinningCmd[totalSkinningCmds];

            int cmdsCount = 0;
            int unskinnedVerticesCount = 0;

            for (int i = 0; i < CpuModels.Length; i++)
            {
                if (!CpuModels[i].IsSkinned)
                {
                    continue;
                }
                Range meshRange = CpuModelGetMeshRange(CpuModels[i]);

                SkinningCmd skinningCmd = new SkinningCmd();
                skinningCmd.InputVertexOffset = unskinnedVerticesCount;
                skinningCmd.VertexCount = GetMeshesVertexCount(meshRange.Start, meshRange.Count);
                skinningCmd.OutputVertexOffset = DrawCommands[meshRange.Start].BaseVertex;

                skinningCmds[cmdsCount++] = skinningCmd;
                
                unskinnedVerticesCount += skinningCmd.VertexCount;
            }
            Array.Resize(ref skinningCmds, cmdsCount);

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

        private static CpuModel GetCpuModel(in ModelLoader.Model model)
        {
            CpuModel newCpuModel = new CpuModel();

            newCpuModel.Nodes = new ModelLoader.Node[model.Nodes.Length];
            model.RootNode.DeepClone(newCpuModel.Nodes, model.Nodes);
            for (int i = 0; i < newCpuModel.Nodes.Length; i++)
            {
                if (newCpuModel.Nodes[i].HasSkin)
                {
                    newCpuModel.IsSkinned = true;
                    break;
                }
            }

            newCpuModel.Animations = new ModelLoader.Animation[model.Animations.Length];
            for (int i = 0; i < newCpuModel.Animations.Length; i++)
            {
                ref readonly ModelLoader.Animation oldAnimation = ref model.Animations[i];
                newCpuModel.Animations[i] = oldAnimation.DeepClone(newCpuModel.Nodes);
            }

            return newCpuModel;
        }

        private static GpuUnskinnedVertex[] GetUnskinnedVertices(ReadOnlySpan<Vector3> vertexPositions, ReadOnlySpan<GpuVertex> vertices)
        {
            GpuUnskinnedVertex[] unskinnedVertices = new GpuUnskinnedVertex[vertexPositions.Length];

            for (int i = 0; i < unskinnedVertices.Length; i++)
            {
                ref readonly GpuVertex vertex = ref vertices[i];
                ref readonly Vector3 vertexPos = ref vertexPositions[i];

                GpuUnskinnedVertex unskinnedVertex = new GpuUnskinnedVertex();
                unskinnedVertex.Position = vertexPos;
                unskinnedVertex.Tangent = vertex.Tangent;
                unskinnedVertex.Normal = vertex.Normal;

                unskinnedVertices[i] = unskinnedVertex;
            }

            return unskinnedVertices;
        }
    }
}
