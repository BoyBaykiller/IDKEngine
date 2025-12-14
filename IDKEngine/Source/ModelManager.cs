using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Bvh;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine;

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
        public ModelLoader.Node SkinnedNode;

        // The offset into the buffer containing animated vertices for the first mesh in SkinnedNode
        public int VertexOffset;
        public int JointMatricesOffset;

        public int BlasId;
    }

    public CpuModel[] CpuModels = [];
    public BVH BVH;

    public BBG.DrawElementsIndirectCommand[] DrawCommands = [];
    public ReadOnlyArray<GpuMeshTransform> MeshTransforms => new ReadOnlyArray<GpuMeshTransform>(meshTransforms);
    public ModelLoader.CpuMaterial[] CpuMaterials = [];
    public GpuMeshInstance[] MeshInstances = [];
    public GpuMaterial[] GpuMaterials = [];
    public GpuMesh[] Meshes = [];
    public GpuVertex[] Vertices = [];
    public NativeMemoryView<Vector3> VertexPositions;
    public uint[] VertexIndices = [];
    public Matrix3x4[] JointMatrices = [];
    public BBG.TypedBuffer<uint> OpaqueMeshInstanceIdBuffer;
    public BBG.TypedBuffer<uint> TransparentMeshInstanceIdBuffer;

    private GpuMeshTransform[] meshTransforms = [];
    private BitArray meshTransformsDirty;

    private BBG.TypedBuffer<BBG.DrawElementsIndirectCommand> drawCommandBuffer;
    private BBG.TypedBuffer<GpuMesh> meshesBuffer;
    private BBG.TypedBuffer<GpuMaterial> materialsBuffer;
    private BBG.TypedBuffer<GpuMeshTransform> meshTransformBuffer;
    private BBG.TypedBuffer<GpuMeshInstance> meshInstanceBuffer;
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
    private BBG.TypedBuffer<byte> meshletsLocalIndicesBuffer;
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
        materialsBuffer = new BBG.TypedBuffer<GpuMaterial>();
        meshTransformBuffer = new BBG.TypedBuffer<GpuMeshTransform>();
        meshInstanceBuffer = new BBG.TypedBuffer<GpuMeshInstance>();
        visibleMeshInstanceIdBuffer = new BBG.TypedBuffer<uint>();
        vertexBuffer = new BBG.TypedBuffer<GpuVertex>();
        vertexPositionsBuffer = new BBG.TypedBuffer<Vector3>();
        vertexIndicesBuffer = new BBG.TypedBuffer<uint>();
        meshletTasksCmdsBuffer = new BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV>();
        meshletTasksCountBuffer = new BBG.TypedBuffer<uint>();
        meshletBuffer = new BBG.TypedBuffer<GpuMeshlet>();
        meshletInfoBuffer = new BBG.TypedBuffer<GpuMeshletInfo>();
        meshletsVertexIndicesBuffer = new BBG.TypedBuffer<uint>();
        meshletsLocalIndicesBuffer = new BBG.TypedBuffer<byte>();
        jointMatricesBuffer = new BBG.TypedBuffer<Matrix3x4>();
        unskinnedVerticesBuffer = new BBG.TypedBuffer<GpuUnskinnedVertex>();
        vertexPositionsHostBuffer = new BBG.TypedBuffer<Vector3>();
        prevVertexPositionsBuffer = new BBG.TypedBuffer<Vector3>();
        OpaqueMeshInstanceIdBuffer = new BBG.TypedBuffer<uint>();
        TransparentMeshInstanceIdBuffer = new BBG.TypedBuffer<uint>();

        drawCommandBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 1);
        meshesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 2);
        materialsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 3);
        meshTransformBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 4);
        meshInstanceBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 5);
        visibleMeshInstanceIdBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 6);
        vertexBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 7);
        vertexPositionsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 8);
        meshletTasksCmdsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 9);
        meshletTasksCountBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 10);
        meshletBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 11);
        meshletInfoBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 12);
        meshletsVertexIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 13);
        meshletsLocalIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 14);
        jointMatricesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 15);
        unskinnedVerticesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 16);
        prevVertexPositionsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 17);

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
        meshletsLocalIndicesBuffer.DownloadElements(out byte[] meshletsLocalIndices);
        unskinnedVerticesBuffer.DownloadElements(out GpuUnskinnedVertex[] unskinnedVertices);

        List<BVH.BlasBuildDesc> blasBuilds = new List<BVH.BlasBuildDesc>();
        List<GpuBlasInstance> blasInstances = new List<GpuBlasInstance>();

        for (int i = 0; i < models.Length; i++)
        {
            ref readonly ModelLoader.Model model = ref models[i];
            ref readonly ModelLoader.GpuModel gpuModel = ref model.GpuModel;
            CpuModel myModel = ConvertToCpuModel(model);

            for (int j = 0; j < myModel.Nodes.Length; j++)
            {
                ModelLoader.Node node = myModel.Nodes[j];
                if (node.HasMeshes)
                {
                    node.MeshTransformsRange.Start += meshTransforms.Length;
                    node.MeshRange.Start += Meshes.Length;
                }
            }
            Helper.ArrayAdd(ref CpuModels, myModel);

            Helper.ArrayAdd(ref DrawCommands, gpuModel.DrawCommands);
            for (int j = DrawCommands.Length - gpuModel.DrawCommands.Length; j < DrawCommands.Length; j++)
            {
                ref BBG.DrawElementsIndirectCommand newDrawCmd = ref DrawCommands[j];
                newDrawCmd.BaseInstance += MeshInstances.Length;
                newDrawCmd.BaseVertex += Vertices.Length;
                newDrawCmd.FirstIndex += VertexIndices.Length;
            }

            Helper.ArrayAdd(ref MeshInstances, gpuModel.MeshInstances);
            for (int j = MeshInstances.Length - gpuModel.MeshInstances.Length; j < MeshInstances.Length; j++)
            {
                ref GpuMeshInstance newMeshInstance = ref MeshInstances[j];
                newMeshInstance.MeshId += Meshes.Length;
                newMeshInstance.MeshTransformId += meshTransforms.Length;
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
                newMeshlet.IndicesOffset += (uint)meshletsLocalIndices.Length;
            }

            GetBlasBuildDescFromModel(myModel, BVH.BlasesDesc.Length + blasBuilds.Count, blasBuilds, blasInstances);

            Helper.ArrayAdd(ref CpuMaterials, model.Materials);
            Helper.ArrayAdd(ref GpuMaterials, gpuModel.Materials);
            Helper.ArrayAdd(ref meshTransforms, gpuModel.MeshTransforms);
            Helper.ArrayAdd(ref Vertices, gpuModel.Vertices);
            Helper.ArrayAdd(ref vertexPositions, gpuModel.VertexPositions);
            Helper.ArrayAdd(ref VertexIndices, gpuModel.VertexIndices);
            Helper.ArrayAdd(ref meshletsInfo, gpuModel.MeshletsInfo);
            Helper.ArrayAdd(ref meshletsVertexIndices, gpuModel.MeshletsVertexIndices);
            Helper.ArrayAdd(ref meshletsLocalIndices, gpuModel.MeshletsLocalIndices);
            Helper.ArrayAdd(ref unskinnedVertices, LoadUnskinnedVertices(model));
        }

        skinningCmds = GetSkinningCommands(out int numJoints, vertexPositions);
        Array.Resize(ref JointMatrices, numJoints);

        UpdateBuffers(vertexPositions, meshlets, meshletsInfo, meshletsVertexIndices, meshletsLocalIndices, unskinnedVertices);

        BVH.SetSourceGeometry(VertexPositions, VertexIndices);
        BVH.Add(blasBuilds, blasInstances);
        BVH.BlasesBuild(BVH.BlasesDesc.Length - blasBuilds.Count, blasBuilds.Count);
        BVH.SetBlasTransforms(meshTransforms);
        BVH.TlasBuild(true);

        UpdateOpqauesAndTransparents();
        meshTransformsDirty = new BitArray(meshTransforms.Length, true);
    }

    public void Draw()
    {
        BBG.Rendering.SetVertexInputDesc(new BBG.Rendering.VertexInputDesc()
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
        UpdateMeshTransformBufferBatched(out anyMeshInstanceMoved);
        ComputeSkinnedPositions(anyNodeMoved);

        // Need to refit BLAS when geometry changed (e.g a skinned node was animated).
        // This check could be made more precise to only refit the specific nodes instead of all
        if (anyNodeMoved)
        {
            for (int i = 0; i < skinningCmds.Length; i++)
            {
                ref readonly SkinningCmd cmd = ref skinningCmds[i];
                BVH.GpuBlasesRefit(cmd.BlasId, 1);

                // TODO: Refit all meshlet bounds (when I have a mesh-shader capable GPU)
            }
        }

        // Need to refit TLAS when a BLAS bounds changed (e.g it moved or was animated)
        if (anyMeshInstanceMoved || anyNodeMoved)
        {
            BVH.TlasBuild();
        }
    }

    public void UpdateOpqauesAndTransparents()
    {
        List<uint> opaqueMeshInstanceIds = new List<uint>();
        List<uint> transparentMeshInstanceIds = new List<uint>();
        for (uint i = 0; i < MeshInstances.Length; i++)
        {
            ref readonly GpuMeshInstance meshInstance = ref MeshInstances[i];
            ref readonly GpuMesh mesh = ref Meshes[meshInstance.MeshId];

            if (GpuMaterials[mesh.MaterialId].HasAlphaBlending())
            {
                transparentMeshInstanceIds.Add(i);
            }
            else
            {
                opaqueMeshInstanceIds.Add(i);
            }
        }
        BBG.Buffer.Recreate(ref OpaqueMeshInstanceIdBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, opaqueMeshInstanceIds);
        BBG.Buffer.Recreate(ref TransparentMeshInstanceIdBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, transparentMeshInstanceIds);
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
                for (int j = 0; j < skin.JointCount; j++)
                {
                    JointMatrices[skinningCmd.JointMatricesOffset + j] = MyMath.Matrix4x4ToTranposed3x4(skin.InverseJointMatrices[j] * skin.Joints[j].GlobalTransform * inverseNodeTransform);
                }
            }
            jointMatricesBuffer.UploadElements(JointMatrices);
        }

        if (fenceCopiedSkinnedVerticesToHost.HasValue && fenceCopiedSkinnedVerticesToHost.Value.TryWait())
        {
            // Wait until skinned vertex positions are downloaded.
            // Then refit the BLASes and update bounding boxes. Refitting on GPU is done elsewhere.

            Task[] tasks = new Task[skinningCmds.Sum(it => 1)];
            int taskCounter = 0;
            for (int i = 0; i < skinningCmds.Length; i++)
            {
                SkinningCmd skinningCmd = skinningCmds[i];

                Range blasRange = new Range(skinningCmd.BlasId, 1);
                for (int j = blasRange.Start; j < blasRange.End; j++)
                {
                    GpuBlasDesc blasDesc = BVH.BlasesDesc[j];

                    tasks[taskCounter++] = Task.Run(() =>
                    {
                        BVH.CpuBlasRefit(blasDesc);
                    });
                }

                Range meshRange = skinningCmd.SkinnedNode.MeshRange;
                for (int j = meshRange.Start; j < meshRange.End; j++)
                {
                    Box bounds = Box.Empty();

                    ref readonly BBG.DrawElementsIndirectCommand cmd = ref DrawCommands[j];
                    for (int k = cmd.BaseVertex; k < cmd.BaseVertex + GetMeshVertexCount(j); k++)
                    {
                        Vector3 vertexPos = VertexPositions[k];
                        bounds.GrowToFit(vertexPos);
                    }

                    Meshes[j].LocalBoundsMin = bounds.Min;
                    Meshes[j].LocalBoundsMax = bounds.Max;
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
                Range meshRange = skinningCmd.SkinnedNode.MeshRange;

                int inputVertexOffset = skinningCmd.VertexOffset;
                int outputVertexOffset = DrawCommands[meshRange.Start].BaseVertex;
                int jointMatricesOffset = skinningCmd.JointMatricesOffset;
                int vertexCount = GetMeshesVertexCount(meshRange);

                BBG.Computing.Compute("Compute Skinned vertices", () =>
                {
                    skinningShaderProgram.Upload(0, (uint)inputVertexOffset);
                    skinningShaderProgram.Upload(1, (uint)outputVertexOffset);
                    skinningShaderProgram.Upload(2, (uint)jointMatricesOffset);
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

    public void SetMeshTransform(int index, in GpuMeshTransform transform)
    {
        meshTransforms[index] = transform;
        meshTransformsDirty[index] = true;
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

    public int GetMeshVertexCount(int index)
    {
        return GetMeshesVertexCount(new Range(index, 1));
    }

    public int GetMeshesVertexCount(Range meshes)
    {
        return GetMeshesVertexCount(DrawCommands, VertexPositions, meshes.Start, meshes.Count);
    }

    public Range GetMeshesVerticesRange(Range meshes)
    {
        return new Range(DrawCommands[meshes.Start].BaseVertex, GetMeshesVertexCount(meshes));
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

    public void UploadMeshTransformBuffer(int start, int count)
    {
        meshTransformBuffer.UploadElements(start, count, meshTransforms[start]);
        for (int i = 0; i < count; i++)
        {
            meshTransformsDirty[start + i] = false;
        }
    }

    private void UpdateMeshTransformBufferBatched(out bool anyMeshInstanceMoved)
    {
        anyMeshInstanceMoved = false;

        int batchedUploadSize = 1 << 8;
        int start = 0;
        int count = meshTransforms.Length;
        int end = start + count;
        for (int i = start; i < end;)
        {
            if (meshTransformsDirty[i])
            {
                int batchStart = i;
                int batchEnd = Math.Min(MyMath.NextMultiple(i, batchedUploadSize), end);

                UploadMeshTransformBuffer(batchStart, batchEnd - batchStart);
                for (int j = batchStart; j < batchEnd; j++)
                {
                    if (meshTransforms[j].DidMove())
                    {
                        meshTransforms[j].SetPrevToCurrentMatrix();
                        meshTransformsDirty[j] = true;
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

                Matrix4 before = node.GlobalTransform;
                node.UpdateGlobalTransform();

                if (node.HasMeshes)
                {
                    Matrix4 after = node.GlobalTransform;
                    Matrix4 diff = after * Matrix4.Invert(before);

                    for (int j = node.MeshTransformsRange.Start; j < node.MeshTransformsRange.End; j++)
                    {
                        GpuMeshTransform transform = meshTransforms[j];

                        transform.ModelMatrix = diff * transform.ModelMatrix;
                        SetMeshTransform(j, transform);
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

                    int index = Algorithms.SortedLowerBound(nodeAnimation.KeyFramesStart, animationTime, MyComparer.LessThan);
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
        ReadOnlySpan<byte> meshletsLocalIndices,
        ReadOnlySpan<GpuUnskinnedVertex> unskinnedVertices)
    {
        BBG.Buffer.Recreate(ref drawCommandBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, DrawCommands);
        BBG.Buffer.Recreate(ref meshesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Meshes);
        BBG.Buffer.Recreate(ref materialsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, GpuMaterials);
        BBG.Buffer.Recreate(ref meshTransformBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, MeshTransforms);
        BBG.Buffer.Recreate(ref meshInstanceBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, MeshInstances);
        BBG.Buffer.Recreate(ref vertexBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, Vertices);
        BBG.Buffer.Recreate(ref vertexPositionsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, vertexPositions);
        BBG.Buffer.Recreate(ref vertexPositionsHostBuffer, BBG.Buffer.MemLocation.HostLocal, BBG.Buffer.MemAccess.MappedCoherent, vertexPositions);
        VertexPositions = new NativeMemoryView<Vector3>(vertexPositionsHostBuffer.Memory, vertexPositionsHostBuffer.NumElements);
        BBG.Buffer.Recreate(ref vertexIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, VertexIndices);
        BBG.Buffer.Recreate(ref meshletBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshlets);
        BBG.Buffer.Recreate(ref meshletInfoBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsInfo);
        BBG.Buffer.Recreate(ref meshletsVertexIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsVertexIndices);
        BBG.Buffer.Recreate(ref meshletsLocalIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, meshletsLocalIndices);
        BBG.Buffer.Recreate(ref jointMatricesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, JointMatrices);
        BBG.Buffer.Recreate(ref unskinnedVerticesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, unskinnedVertices);
        BBG.Buffer.Recreate(ref visibleMeshInstanceIdBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, MeshInstances.Length * 6);
        BBG.Buffer.Recreate(ref meshletTasksCmdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, MeshInstances.Length * 6);
        BBG.Buffer.Recreate(ref prevVertexPositionsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, vertexPositions);
    }

    private SkinningCmd[] GetSkinningCommands(out int numJoints, ReadOnlySpan<Vector3> vertexPositions)
    {
        numJoints = 0;

        List<SkinningCmd> skinningCmds = new List<SkinningCmd>();
        int unskinnedVerticesCount = 0;
        int blasCount = 0; // counting must match logic in GetBlasBuildDescFromModel
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
                    skinningCmd.JointMatricesOffset = numJoints;
                    skinningCmd.BlasId = blasCount;
                    skinningCmds.Add(skinningCmd);

                    int vertexCount = GetMeshesVertexCount(DrawCommands, vertexPositions, node.MeshRange.Start, node.MeshRange.Count);

                    unskinnedVerticesCount += vertexCount;
                    numJoints += node.Skin.JointCount;
                }

                if (node.HasMeshes)
                {
                    blasCount++;
                }
            }
        }

        return skinningCmds.ToArray();
    }

    private void GetBlasBuildDescFromModel(CpuModel model, int blasOffset, List<BVH.BlasBuildDesc> blasBuilds, List<GpuBlasInstance> blasInstances)
    {
        for (int i = 0; i < model.Nodes.Length; i++)
        {
            ModelLoader.Node node = model.Nodes[i];
            if (node.HasMeshes)
            {
                Range meshRange = node.MeshRange;
                Range transformRange = node.MeshTransformsRange;

                BVH.BlasBuildDesc blasBuildDesc = new BVH.BlasBuildDesc();
                blasBuildDesc.Geometries = new BVH.BlasBuildDesc.Geometry[meshRange.Count];
                blasBuildDesc.IsRefittable = node.HasSkin;

                for (int j = meshRange.Start; j < meshRange.End; j++)
                {
                    ref readonly BBG.DrawElementsIndirectCommand cmd = ref DrawCommands[j];

                    BVH.BlasBuildDesc.Geometry geometryDesc = new BVH.BlasBuildDesc.Geometry();
                    geometryDesc.TriangleCount = cmd.IndexCount / 3;
                    geometryDesc.TriangleOffset = cmd.FirstIndex / 3;
                    geometryDesc.VertexOffset = cmd.BaseVertex;
                    geometryDesc.MeshId = j;

                    blasBuildDesc.Geometries[j - meshRange.Start] = geometryDesc;
                }
                blasBuilds.Add(blasBuildDesc);

                for (int j = transformRange.Start; j < transformRange.End; j++)
                {
                    GpuBlasInstance blasInstance = new GpuBlasInstance();
                    blasInstance.BlasId = blasOffset;
                    blasInstance.MeshTransformId = j;
                    blasInstances.Add(blasInstance);
                }

                blasOffset++;
            }
        }
    }

    public void Dispose()
    {
        drawCommandBuffer.Dispose();
        meshesBuffer.Dispose();
        meshInstanceBuffer.Dispose();
        meshTransformBuffer.Dispose();
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
        meshletsLocalIndicesBuffer.Dispose();
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
                Range meshRange = node.MeshRange;
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

    private static CpuModel ConvertToCpuModel(in ModelLoader.Model model)
    {
        CpuModel newCpuModel = new CpuModel();

        newCpuModel.Nodes = model.RootNode.DeepClone(model.Nodes);

        newCpuModel.Animations = new ModelLoader.ModelAnimation[model.Animations.Length];
        for (int i = 0; i < newCpuModel.Animations.Length; i++)
        {
            ref readonly ModelLoader.ModelAnimation oldAnimation = ref model.Animations[i];
            newCpuModel.Animations[i] = oldAnimation.DeepClone(newCpuModel.Nodes);
        }

        newCpuModel.EnabledAnimations = new BitArray(newCpuModel.Animations.Length, true);

        return newCpuModel;
    }

    public static Range GetNodeMeshRangeRecursive(ModelLoader.Node node)
    {
        int min = int.MaxValue;
        int max = -1;
        ModelLoader.Node.Traverse(node, (node) =>
        {
            if (node.HasMeshes)
            {
                min = Math.Min(min, node.MeshRange.Start);
                max = Math.Max(max, node.MeshRange.End);
            }
        });

        if (max == -1)
        {
            return new Range(0, 0);
        }

        return new Range(min, max - min);
    }

    private static int GetMeshesVertexCount(ReadOnlySpan<BBG.DrawElementsIndirectCommand> drawCmds, ReadOnlySpan<Vector3> vertexPositions, int startMesh, int count = 1)
    {
        int baseVertex = drawCmds[startMesh].BaseVertex;
        int nextBaseVertex = startMesh + count == drawCmds.Length ? vertexPositions.Length : drawCmds[startMesh + count].BaseVertex;
        return nextBaseVertex - baseVertex;
    }
}
