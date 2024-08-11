using System;
using System.Linq;
using System.Diagnostics;
using System.Collections;
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
            public bool AnyNodeHasSkin;

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

        public BBG.DrawElementsIndirectCommand[] DrawCommands = Array.Empty<BBG.DrawElementsIndirectCommand>();
        private readonly BBG.TypedBuffer<BBG.DrawElementsIndirectCommand> drawCommandBuffer;

        public GpuMesh[] Meshes = Array.Empty<GpuMesh>();
        private readonly BBG.TypedBuffer<GpuMesh> meshBuffer;

        public ReadOnlyArray<GpuMeshInstance> MeshInstances => new ReadOnlyArray<GpuMeshInstance>(meshInstances);
        private GpuMeshInstance[] meshInstances = Array.Empty<GpuMeshInstance>();
        private readonly BBG.TypedBuffer<GpuMeshInstance> meshInstanceBuffer;
        private BitArray meshInstancesDirty;

        public GpuMaterial[] Materials = Array.Empty<GpuMaterial>();
        private readonly BBG.TypedBuffer<GpuMaterial> materialBuffer;

        public GpuVertex[] Vertices = Array.Empty<GpuVertex>();
        private readonly BBG.TypedBuffer<GpuVertex> vertexBuffer;

        public Vector3[] VertexPositions = Array.Empty<Vector3>();
        private readonly BBG.TypedBuffer<Vector3> vertexPositionBuffer;

        public uint[] VertexIndices = Array.Empty<uint>();
        private readonly BBG.TypedBuffer<uint> vertexIndicesBuffer;

        public GpuMeshlet[] Meshlets = Array.Empty<GpuMeshlet>();
        private readonly BBG.TypedBuffer<GpuMeshlet> meshletBuffer;

        public GpuMeshletInfo[] MeshletsInfo = Array.Empty<GpuMeshletInfo>();
        private readonly BBG.TypedBuffer<GpuMeshletInfo> meshletInfoBuffer;

        public uint[] MeshletsVertexIndices = Array.Empty<uint>();
        private readonly BBG.TypedBuffer<uint> meshletsVertexIndicesBuffer;

        public byte[] MeshletsLocalIndices = Array.Empty<byte>();
        private readonly BBG.TypedBuffer<byte> meshletsPrimitiveIndicesBuffer;

        public Vector4i[] JointIndices = Array.Empty<Vector4i>();
        private readonly BBG.TypedBuffer<Vector4i> jointIndicesBuffer;

        public Vector4[] JointWeights = Array.Empty<Vector4>();
        private readonly BBG.TypedBuffer<Vector4> jointWeightsBuffer;

        public Matrix3x4[] JointMatrices = Array.Empty<Matrix3x4>();
        private readonly BBG.TypedBuffer<Matrix3x4> jointMatricesBuffer;

        private readonly BBG.TypedBuffer<GpuUnskinnedVertex> unskinnedVerticesBuffer;
        private readonly BBG.TypedBuffer<BBG.DrawArraysIndirectCommand> skinningCommandBuffer;
        private readonly BBG.TypedBuffer<BBG.DrawMeshTasksIndirectCommandNV> meshletTasksCmdsBuffer;
        private readonly BBG.TypedBuffer<int> meshletTasksCountBuffer;
        private readonly BBG.TypedBuffer<uint> visibleMeshInstanceBuffer;
        private readonly BBG.TypedBuffer<Vector3> prevVertexPositionBuffer;

        public CpuModel[] Models = Array.Empty<CpuModel>();

        public BVH BVH;

        private readonly BBG.AbstractShaderProgram skinningShaderProgram;
        private readonly Stopwatch globalAnimationsTimer = new Stopwatch();
        private bool runSkinningShader;
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
            jointIndicesBuffer = new BBG.TypedBuffer<Vector4i>();
            jointWeightsBuffer = new BBG.TypedBuffer<Vector4>();
            jointMatricesBuffer = new BBG.TypedBuffer<Matrix3x4>();
            unskinnedVerticesBuffer = new BBG.TypedBuffer<GpuUnskinnedVertex>();
            skinningCommandBuffer = new BBG.TypedBuffer<BBG.DrawArraysIndirectCommand>();
            prevVertexPositionBuffer = new BBG.TypedBuffer<Vector3>();

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
            jointIndicesBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 18);
            jointWeightsBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 19);
            jointMatricesBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 20);
            unskinnedVerticesBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 21);
            prevVertexPositionBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 22);

            BVH = new BVH();

            skinningShaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Vertex, "Skinning/vertex.glsl"));
            RunAnimations = true;
        }

        public void Add(params ModelLoader.Model?[] models)
        {
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

                Helper.ArrayAdd(ref JointIndices, gpuModel.JointIndices);
                for (int j = JointIndices.Length - gpuModel.JointIndices.Length; j < JointIndices.Length; j++)
                {
                    JointIndices[j] += new Vector4i(JointMatrices.Length);
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
                Helper.ArrayAdd(ref Models, [myModel]);

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
                    newMeshInstance.MeshIndex += Meshes.Length;
                }

                Helper.ArrayAdd(ref Meshes, gpuModel.Meshes);
                for (int j = Meshes.Length - gpuModel.Meshes.Length; j < Meshes.Length; j++)
                {
                    ref GpuMesh newMesh = ref Meshes[j];
                    newMesh.MaterialIndex += Materials.Length;
                    newMesh.MeshletsStart += Meshlets.Length;
                }

                Helper.ArrayAdd(ref Meshlets, gpuModel.Meshlets);
                for (int j = Meshlets.Length - gpuModel.Meshlets.Length; j < Meshlets.Length; j++)
                {
                    ref GpuMeshlet newMeshlet = ref Meshlets[j];
                    newMeshlet.VertexOffset += (uint)MeshletsVertexIndices.Length;
                    newMeshlet.IndicesOffset += (uint)MeshletsLocalIndices.Length;
                }

                Helper.ArrayAdd(ref Materials, gpuModel.Materials);
                Helper.ArrayAdd(ref Vertices, gpuModel.Vertices);
                Helper.ArrayAdd(ref VertexPositions, gpuModel.VertexPositions);
                Helper.ArrayAdd(ref VertexIndices, gpuModel.VertexIndices);
                Helper.ArrayAdd(ref MeshletsInfo, gpuModel.MeshletsInfo);
                Helper.ArrayAdd(ref MeshletsVertexIndices, gpuModel.MeshletsVertexIndices);
                Helper.ArrayAdd(ref MeshletsLocalIndices, gpuModel.MeshletsLocalIndices);
                Helper.ArrayAdd(ref JointWeights, gpuModel.JointWeights);
                Array.Resize(ref JointMatrices, JointMatrices.Length + model.GetNumJoints());
            }

            if (models.Length > 0)
            {
                ReadOnlySpan<BBG.DrawElementsIndirectCommand> newDrawCommands = new ReadOnlySpan<BBG.DrawElementsIndirectCommand>(DrawCommands, prevDrawCommandsLength, DrawCommands.Length - prevDrawCommandsLength);
                BVH.AddMeshes(newDrawCommands, VertexPositions, VertexIndices, DrawCommands, meshInstances);

                uint bvhNodesExclusiveSum = 0;
                for (int i = 0; i < DrawCommands.Length; i++)
                {
                    // Adjust root node index in context of all Nodes
                    Meshes[i].BlasRootNodeOffset = bvhNodesExclusiveSum;
                    bvhNodesExclusiveSum += (uint)BVH.Tlas.Blases[i].Nodes.Length;
                }
            }
            
            AllocateAndUploadAllBuffers();
            meshInstancesDirty = new BitArray(meshInstances.Length, true);
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

        public unsafe void ComputeSkinnedPositions()
        {
            if (RunAnimations)
            {
                int jointsProcessed = 0;
                for (int i = 0; i < Models.Length; i++)
                {
                    ref readonly CpuModel cpuModel = ref Models[i];
                    if (!cpuModel.AnyNodeHasSkin)
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

            if (runSkinningShader)
            {
                BBG.Rendering.Render("Compute Skinned vertices", new BBG.Rendering.NoRenderAttachmentsParams(), new BBG.Rendering.GraphicsPipelineState(), () =>
                {
                    BBG.Cmd.UseShaderProgram(skinningShaderProgram);
                    BBG.Rendering.MultiDrawNonIndexed(skinningCommandBuffer, BBG.Rendering.Topology.Points, skinningCommandBuffer.NumElements, sizeof(BBG.DrawArraysIndirectCommand));
                    BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);
                });

                if (!RunAnimations)
                {
                    // Skinning shader must stop with one frame delay after Animations have been stoped so velocity becomes zero
                    runSkinningShader = false;
                }
            }
        }

        public void Update(out bool anyMeshInstancedUploaded)
        {
            UpdateNodeAnimations();
            UpdateNodeHierarchy();
            UpdateMeshInstanceBufferBatched(out anyMeshInstancedUploaded);
        }

        public void SetMeshInstance(int index, in GpuMeshInstance meshInstance)
        {
            meshInstances[index] = meshInstance;
            meshInstancesDirty[index] = true;
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
        
        private void UpdateMeshInstanceBufferBatched(out bool anyMeshInstancedUploaded)
        {
            anyMeshInstancedUploaded = false;

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
                            anyMeshInstancedUploaded = true;
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
            for (int i = 0; i < Models.Length; i++)
            {
                ref readonly CpuModel cpuModel = ref Models[i];
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
        
        private void UpdateNodeAnimations()
        {
            if (!RunAnimations)
            {
                return;
            }

            float globalTime = (float)globalAnimationsTimer.Elapsed.TotalSeconds;
            for (int i = 0; i < Models.Length; i++)
            {
                ref readonly CpuModel cpuModel = ref Models[i];
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

        private unsafe void AllocateAndUploadAllBuffers()
        {
            drawCommandBuffer.MutableAllocateElements(DrawCommands);
            meshBuffer.MutableAllocateElements(Meshes);
            meshInstanceBuffer.MutableAllocateElements(meshInstances);
            materialBuffer.MutableAllocateElements(Materials);
            vertexBuffer.MutableAllocateElements(Vertices);
            vertexPositionBuffer.MutableAllocateElements(VertexPositions);
            vertexIndicesBuffer.MutableAllocateElements(VertexIndices);

            meshletBuffer.MutableAllocateElements(Meshlets);
            meshletInfoBuffer.MutableAllocateElements(MeshletsInfo);
            meshletsVertexIndicesBuffer.MutableAllocateElements(MeshletsVertexIndices);
            meshletsPrimitiveIndicesBuffer.MutableAllocateElements(MeshletsLocalIndices);

            jointIndicesBuffer.MutableAllocateElements(JointIndices);
            jointWeightsBuffer.MutableAllocateElements(JointWeights);
            jointMatricesBuffer.MutableAllocateElements(JointMatrices.Length);

            GetSkinningCommandsAndUnskinnedVertices(out BBG.DrawArraysIndirectCommand[] skinningCmds, out GpuUnskinnedVertex[] unskinnedVertices);
            skinningCommandBuffer.MutableAllocateElements(skinningCmds);
            unskinnedVerticesBuffer.MutableAllocateElements(unskinnedVertices);

            visibleMeshInstanceBuffer.MutableAllocateElements(meshInstances.Length * 6); 
            meshletTasksCmdsBuffer.MutableAllocateElements(meshInstances.Length * 6);
            meshletTasksCountBuffer.MutableAllocateElements(1);

            prevVertexPositionBuffer.MutableAllocateElements(VertexPositions);
        }

        private Range CpuModelGetMeshRange(in CpuModel model)
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
                    min = Math.Min(min, first.MeshIndex);
                    max = Math.Max(max, last.MeshIndex + 1);
                }
            }

            return new Range(min, max - min);
        }

        private int GetMeshesVertexCount(int startMesh, int count = 1)
        {
            int baseVertex = DrawCommands[startMesh].BaseVertex;
            int nextBaseVertex = startMesh + count == DrawCommands.Length ? VertexPositions.Length : DrawCommands[startMesh + count].BaseVertex;
            return nextBaseVertex - baseVertex;
        }

        private void GetSkinningCommandsAndUnskinnedVertices(out BBG.DrawArraysIndirectCommand[] skinningCmds, out GpuUnskinnedVertex[] unskinnedVertices)
        {
            int totalSkinningVertices = Models.Sum((CpuModel model) =>
            {
                if (!model.AnyNodeHasSkin)
                {
                    return 0;
                }

                Range meshRange = CpuModelGetMeshRange(model);
                return GetMeshesVertexCount(meshRange.Start, meshRange.Count);
            });
            int totalSkinningCmds = Models.Sum(model => model.AnyNodeHasSkin ? 1 : 0);

            int unskinnedVerticesCounter = 0;
            int skinningCmdCounter = 0;
            skinningCmds = new BBG.DrawArraysIndirectCommand[totalSkinningCmds];
            unskinnedVertices = new GpuUnskinnedVertex[totalSkinningVertices];

            for (int i = 0; i < Models.Length; i++)
            {
                if (!Models[i].AnyNodeHasSkin)
                {
                    continue;
                }

                Range meshRange = CpuModelGetMeshRange(Models[i]);

                BBG.DrawArraysIndirectCommand skinningCmd = new BBG.DrawArraysIndirectCommand();
                skinningCmd.First = unskinnedVerticesCounter;
                skinningCmd.Count = GetMeshesVertexCount(meshRange.Start, meshRange.Count);
                skinningCmd.InstanceCount = 1;

                // this field is accessible in the shader, we abuse it and assign it the offset used for writing out the skinned vertex
                skinningCmd.BaseInstance = (uint)(DrawCommands[meshRange.Start].BaseVertex - skinningCmd.First);
                
                skinningCmds[skinningCmdCounter++] = skinningCmd;

                for (int j = meshRange.Start; j < meshRange.End; j++)
                {
                    ref readonly BBG.DrawElementsIndirectCommand drawCmd = ref DrawCommands[j];
                    for (int k = drawCmd.BaseVertex; k < drawCmd.BaseVertex + GetMeshesVertexCount(j); k++)
                    {
                        ref readonly GpuVertex vertex = ref Vertices[k];
                        ref readonly Vector3 vertexPos = ref VertexPositions[k];

                        GpuUnskinnedVertex unskinnedVertex = new GpuUnskinnedVertex();
                        unskinnedVertex.Position = vertexPos;
                        unskinnedVertex.Tangent = vertex.Tangent;
                        unskinnedVertex.Normal = vertex.Normal;

                        unskinnedVertices[unskinnedVerticesCounter++] = unskinnedVertex;
                    }
                }
            }
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
            jointIndicesBuffer.Dispose();
            jointWeightsBuffer.Dispose();
            jointMatricesBuffer.Dispose();
            unskinnedVerticesBuffer.Dispose();
            skinningCommandBuffer.Dispose();
            prevVertexPositionBuffer.Dispose();

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
                    newCpuModel.AnyNodeHasSkin = true;
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
    }
}
