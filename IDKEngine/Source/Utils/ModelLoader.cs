using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using SharpGLTF.Schema2;
using SharpGLTF.Materials;
using Ktx;
using Meshoptimizer;
using BBLogger;
using BBOpenGL;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;
using GLTexture = BBOpenGL.BBG.Texture;
using GLSampler = BBOpenGL.BBG.Sampler;
using GltfTexture = SharpGLTF.Schema2.Texture;
using GltfSampler = SharpGLTF.Schema2.TextureSampler;
using GltfNode = SharpGLTF.Schema2.Node;
using GltfAnimation = SharpGLTF.Schema2.Animation;

namespace IDKEngine.Utils;

public static unsafe class ModelLoader
{
    public static readonly string[] SupportedExtensions = [
        "KHR_materials_emissive_strength",
        "KHR_materials_volume",
        "KHR_materials_ior",
        "KHR_materials_transmission",
        "EXT_mesh_gpu_instancing",
        "KHR_texture_basisu",
        "IDK_BC5_normal_metallicRoughness"
    ];

    public enum TextureType : int
    {
        BaseColor,
        MetallicRoughness,
        Normal,
        Emissive,
        Transmission,
        Count,
    }

    public enum AlphaMode : int
    {
        Opaque = SharpGLTF.Schema2.AlphaMode.OPAQUE,
        Mask = SharpGLTF.Schema2.AlphaMode.MASK,
        Blend = SharpGLTF.Schema2.AlphaMode.BLEND,
    }

    public record struct Model
    {
        public readonly Node RootNode => Nodes[0];
        public Node[] Nodes;

        public ModelAnimation[] Animations;
        public CpuMaterial[] Materials;
        public GpuModel GpuModel;
    }

    public record struct GpuModel
    {
        public BBG.DrawElementsIndirectCommand[] DrawCommands;
        public GpuMesh[] Meshes;
        public GpuMeshInstance[] MeshInstances;
        public GpuMaterial[] Materials;

        // Base geometry
        public GpuVertex[] Vertices;
        public Vector3[] VertexPositions;
        public uint[] VertexIndices;

        // Meshlet-rendering specific data
        public GpuMeshlet[] Meshlets;
        public GpuMeshletInfo[] MeshletsInfo;
        public uint[] MeshletsVertexIndices;
        public byte[] MeshletsLocalIndices;

        // Animations
        public Vector4i[] JointIndices;
        public Vector4[] JointWeights;
    }

    public class Node
    {
        public bool IsRoot => Parent == null;
        public bool IsLeaf => Children.Length == 0;

        public bool HasMeshInstances => MeshInstanceRange.Count > 0;
        public bool HasSkin => Skin.Joints != null; // Skin implies HasMeshInstances

        public Transformation LocalTransform
        {
            get => _localTransform;

            set
            {
                if (_localTransform != value)
                {
                    MarkDirty();
                    _localTransform = value;
                }
            }
        }

        private Transformation _localTransform = new Transformation();
        public Matrix4 GlobalTransform { get; private set; } = Matrix4.Identity; // local * parent.Global

        public Node Parent;
        public Node[] Children = [];
        public string Name = string.Empty;
        public int ArrayIndex;

        public Skin Skin;
        public Range MeshInstanceRange;

        private bool isDirty = true;
        private bool isAscendantOfDirty = false;

        public void UpdateGlobalTransform()
        {
            if (IsRoot)
            {
                GlobalTransform = LocalTransform.GetMatrix();
            }
            else
            {
                GlobalTransform = LocalTransform.GetMatrix() * Parent.GlobalTransform;
            }
        }

        public void DeepClone(ReadOnlySpan<Node> srcNodes, Span<Node> dstNodes)
        {
            HierarchyToArray(DeepCloneRecursive(this), dstNodes);

            // Copying Skin needs to be done after all Nodes have been copied
            for (int i = 0; i < dstNodes.Length; i++)
            {
                if (dstNodes[i].HasSkin)
                {
                    dstNodes[i].Skin = srcNodes[i].Skin.DeepClone(dstNodes);
                }
            }

            static Node DeepCloneRecursive(Node source)
            {
                Node newNode = new Node();
                newNode.Name = new string(source.Name);
                newNode.ArrayIndex = source.ArrayIndex;
                newNode.LocalTransform = source.LocalTransform;
                newNode.GlobalTransform = source.GlobalTransform;
                newNode.MeshInstanceRange = source.MeshInstanceRange;
                newNode.Skin = source.Skin;
                newNode.isDirty = source.isDirty;

                newNode.Children = new Node[source.Children.Length];
                for (int i = 0; i < source.Children.Length; i++)
                {
                    newNode.Children[i] = DeepCloneRecursive(source.Children[i]);
                    newNode.Children[i].Parent = newNode;
                }

                return newNode;
            }
        }

        private void MarkDirty()
        {
            if (isDirty)
            {
                return;
            }

            isDirty = true;
            MarkParentsDirty(this);

            static void MarkParentsDirty(Node node)
            {
                if (!node.IsRoot && !node.Parent.isAscendantOfDirty)
                {
                    node.Parent.isAscendantOfDirty = true;
                    MarkParentsDirty(node.Parent);
                }
            }
        }

        /// <summary>
        /// Traverses into all dirty parents and marks them as no longer dirty. The caller is expected to call
        /// <see cref="UpdateGlobalTransform"/> inside <paramref name="updateFunc"/>
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="updateFunc"></param>
        public static void TraverseUpdate(Node parent, Action<Node> updateFunc)
        {
            if (!parent.isDirty && !parent.isAscendantOfDirty)
            {
                return;
            }

            if (parent.isDirty)
            {
                updateFunc(parent);
            }

            for (int i = 0; i < parent.Children.Length; i++)
            {
                Node child = parent.Children[i];
                if (parent.isDirty)
                {
                    child.isDirty = true; // if parent is dirty children automatically are too
                }

                TraverseUpdate(child, updateFunc);
            }

            parent.isDirty = false;
            parent.isAscendantOfDirty = false;
        }

        public static void Traverse(Node node, Action<Node> funcOnNode)
        {
            funcOnNode(node);
            for (int i = 0; i < node.Children.Length; i++)
            {
                Traverse(node.Children[i], funcOnNode);
            }
        }

        public static void HierarchyToArray(Node parent, Span<Node> nodes)
        {
            Span<Node>* ptrNodes = &nodes;
            Traverse(parent, (node) =>
            {
                (*ptrNodes)[node.ArrayIndex] = node;
            });
        }
    }

    /// <summary>
    /// See <see href="https://github.com/zeux/meshoptimizer"></see> for details.
    /// When gltfpack is run these optimizations are already applied so doing them again is useless
    /// </summary>
    public record struct OptimizationSettings
    {
        /// <summary>
        /// <see href="https://github.com/zeux/meshoptimizer/tree/master?tab=readme-ov-file#indexing"></see>
        /// </summary>
        public bool VertexRemapOptimization;

        /// <summary>
        /// <see href="https://github.com/zeux/meshoptimizer/tree/master?tab=readme-ov-file#vertex-cache-optimization"></see>
        /// </summary>
        public bool VertexCacheOptimization;

        /// <summary>
        /// <see href="https://github.com/zeux/meshoptimizer/tree/master?tab=readme-ov-file#vertex-fetch-optimization"></see>
        /// </summary>
        public bool VertexFetchOptimization;

        public static readonly OptimizationSettings AllTurnedOff = new OptimizationSettings()
        {
            VertexRemapOptimization = false,
            VertexCacheOptimization = false,
            VertexFetchOptimization = false,
        };

        public static readonly OptimizationSettings AllTurnedOn = new OptimizationSettings()
        {
            VertexRemapOptimization = true,
            VertexCacheOptimization = true,
            VertexFetchOptimization = true,
        };

        public static readonly OptimizationSettings Recommended = new OptimizationSettings()
        {
            VertexRemapOptimization = false,
            VertexCacheOptimization = true,
            VertexFetchOptimization = false,
        };
    }

    public record struct ModelAnimation
    {
        public readonly float Duration => End - Start;

        public float Start; // Min of node animations start
        public float End; // Max of node animations end

        public string Name;

        public NodeAnimation[] NodeAnimations;

        public readonly ModelAnimation DeepClone(ReadOnlySpan<Node> srcNewNodes)
        {
            ModelAnimation animation = new ModelAnimation();
            animation.Start = Start;
            animation.End = End;
            animation.Name = new string(Name);
            animation.NodeAnimations = new NodeAnimation[NodeAnimations.Length];
            for (int i = 0; i < animation.NodeAnimations.Length; i++)
            {
                animation.NodeAnimations[i] = NodeAnimations[i].DeepClone(srcNewNodes);
            }

            return animation;
        }
    }

    public record struct NodeAnimation
    {
        public enum AnimationType : int
        {
            Scale = PropertyPath.scale,
            Rotation = PropertyPath.rotation,
            Translation = PropertyPath.translation,
        }

        public enum InterpolationMode : int
        {
            Step = AnimationInterpolationMode.STEP,
            Linear = AnimationInterpolationMode.LINEAR,
        }

        public readonly float Start => KeyFramesStart[0];
        public readonly float End => KeyFramesStart[KeyFramesStart.Length - 1];
        public readonly float Duration => End - Start;

        public Node TargetNode;
        public AnimationType Type;
        public InterpolationMode Mode;
        public float[] KeyFramesStart;  // time in seconds from the beginning of the animation when the Keyframe at at that index should be used
        public byte[] RawKeyFramesData; // interpreted differently based on PropertyPath

        public readonly Span<Vector3> GetKeyFrameDataAsVec3()
        {
            if (Type != AnimationType.Translation && Type != AnimationType.Scale)
            {
                throw new ArgumentException($"{nameof(Type)} = {Type} is not meant to be interpreted as Vector3");
            }

            return MemoryMarshal.Cast<byte, Vector3>((Span<byte>)RawKeyFramesData);
        }

        public readonly Span<Quaternion> GetKeyFrameDataAsQuaternion()
        {
            if (Type != AnimationType.Rotation)
            {
                throw new ArgumentException($"{nameof(Type)} = {Type} is not meant to be interpreted as Quaternion");
            }

            return MemoryMarshal.Cast<byte, Quaternion>((Span<byte>)RawKeyFramesData);
        }

        public readonly NodeAnimation DeepClone(ReadOnlySpan<Node> srcNewNodes)
        {
            NodeAnimation sampler = new NodeAnimation();
            sampler.TargetNode = srcNewNodes[TargetNode.ArrayIndex];
            sampler.Type = Type;
            sampler.Mode = Mode;
            sampler.KeyFramesStart = KeyFramesStart.DeepClone();
            sampler.RawKeyFramesData = RawKeyFramesData.DeepClone();

            return sampler;
        }
    }

    public record struct Skin
    {
        public readonly int JointsCount => Joints.Length;

        public Node[] Joints;
        public Matrix4[] InverseJointMatrices;

        public readonly Skin DeepClone(ReadOnlySpan<Node> newNodes)
        {
            Skin skin = new Skin();
            skin.Joints = new Node[JointsCount];
            for (int i = 0; i < JointsCount; i++)
            {
                skin.Joints[i] = newNodes[Joints[i].ArrayIndex];
            }
            skin.InverseJointMatrices = InverseJointMatrices.DeepClone();

            return skin;
        }
    }

    public record struct CpuMaterial
    {
        public const int TEXTURE_COUNT = (int)TextureType.Count;

        public int HasFallbackPixelsBits;
        public SampledTextureArray SampledTextures;
        public string Name;

        public readonly bool HasFallbackPixels(TextureType type)
        {
            return (HasFallbackPixelsBits & (1 << (int)type)) != 0;
        }

        public void SetHasFallbackPixels(TextureType type, bool value)
        {
            Algorithms.SetBit(ref HasFallbackPixelsBits, (int)type, value);
        }

        [InlineArray(TEXTURE_COUNT)]
        public struct SampledTextureArray
        {
            public SampledTexture _element;
        }
    }

    public record struct SampledTexture : IDisposable
    {
        public GLTexture Texture;
        public GLSampler Sampler;

        public readonly void Deconstruct(out GLTexture texture, out GLSampler sampler)
        {
            texture = Texture;
            sampler = Sampler;
        }

        public readonly void Dispose()
        {
            Sampler.Dispose();
            Texture.Dispose();
        }
    }

    private record struct BindlessSampledTexture
    {
        public SampledTexture SampledTexture;
        public GLTexture.BindlessHandle BindlessHandle;
    }

    public record struct MaterialParams
    {
        public readonly bool IsVolumetric => ThicknessFactor > 0.0f;

        public Vector4 BaseColorFactor = new Vector4(1.0f);
        public float RoughnessFactor = 1.0f;
        public float MetallicFactor = 1.0f;
        public Vector3 EmissiveFactor = new Vector3(0.0f);
        public float TransmissionFactor = 0.0f;
        public float AlphaCutoff = 0.5f;
        public Vector3 Absorbance = new Vector3(0.0f);
        public float IOR = 1.5f;
        public AlphaMode AlphaMode = AlphaMode.Opaque;
        public float ThicknessFactor = 0.0f;

        public MaterialParams()
        {
        }
    }

    private record struct MaterialDesc
    {
        public MaterialParams MaterialParams = new MaterialParams();
        public SampledImageArray SampledImages;
        public string Name;

        public MaterialDesc()
        {
        }

        [InlineArray((int)TextureType.Count)]
        public struct SampledImageArray
        {
            public SampledImage _element;
        }
    }

    private record struct SampledImage
    {
        public readonly bool HasImage => GltfImage != null;

        public Image GltfImage;
        public GLSampler.SamplerState SamplerState;
    }

    private record struct MeshGeometry
    {
        public MeshletData MeshletData;
        public GpuMeshlet[] Meshlets;
        public GpuMeshletInfo[] MeshletsInfo;

        public VertexData VertexData;
        public uint[] VertexIndices;
    }

    private record struct MeshletData
    {
        public Meshopt.Meshlet[] Meshlets;
        public int MeshletsLength;

        public uint[] VertexIndices;
        public int VertexIndicesLength;

        public byte[] LocalIndices;
        public int LocalIndicesLength;
    }

    private record struct VertexData
    {
        public GpuVertex[] Vertices;
        public Vector3[] Positons;

        public Vector4i[] JointIndices;
        public Vector4[] JointWeights;
    }

    private record struct MeshPrimitiveDesc
    {
        public readonly bool HasNormalAccessor => NormalAccessor != -1;
        public readonly bool HasTexCoordAccessor => TexCoordAccessor != -1;
        public readonly bool HasJointsAccessor => JointsAccessor != -1;
        public readonly bool HasWeightsAccessor => WeightsAccessor != -1;
        public readonly bool HasIndexAccessor => IndexAccessor != -1;

        public int PositionAccessor;
        public int NormalAccessor = -1;
        public int TexCoordAccessor = -1;
        public int JointsAccessor = -1;
        public int WeightsAccessor = -1;
        public int IndexAccessor = -1;

        public MeshPrimitiveDesc()
        {
        }
    }

    private struct USVec4
    {
        public fixed ushort Data[4];
    }

    private struct BVec4
    {
        public fixed byte Data[4];
    }

    public static event Action TextureLoaded;

    public static Model? LoadGltfFromFile(string path)
    {
        return LoadGltfFromFile(path, Matrix4.Identity);
    }

    public static Model? LoadGltfFromFile(string path, in Matrix4 rootTransform)
    {
        return LoadGltfFromFile(path, rootTransform, OptimizationSettings.Recommended);
    }

    public static Model? LoadGltfFromFile(string path, in Matrix4 rootTransform, OptimizationSettings optimizationSettings)
    {
        if (!File.Exists(path))
        {
            Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
            return null;
        }

        ModelRoot gltf = ModelRoot.Load(path, new ReadSettings() { Validation = SharpGLTF.Validation.ValidationMode.Skip });

        string fileName = Path.GetFileName(path);
        foreach (string ext in gltf.ExtensionsUsed)
        {
            if (SupportedExtensions.Contains(ext))
            {
                Logger.Log(Logger.LogLevel.Info, $"Model \"{fileName}\" uses extension {ext}");
            }
            else
            {
                Logger.Log(Logger.LogLevel.Warn, $"Model \"{fileName}\" uses extension {ext} which is not supported");
            }
        }

        if (!GtlfpackWrapper.IsCompressed(gltf))
        {
            Logger.Log(Logger.LogLevel.Warn, $"Model \"{fileName}\" is uncompressed");
        }

        bool usesExtBc5NormalMetallicRoughness = gltf.ExtensionsUsed.Contains("IDK_BC5_normal_metallicRoughness");
        if (gltf.ExtensionsUsed.Contains("KHR_texture_basisu") && !usesExtBc5NormalMetallicRoughness)
        {
            Logger.Log(Logger.LogLevel.Warn, $"Model \"{fileName}\" uses extension KHR_texture_basisu without IDK_BC5_normal_metallicRoughness,\n" +
                                              "causing normal and metallicRoughness textures with a suboptimal format (BC7) and potentially visible error.\n" +
                                              "Optimal compression can be achieved with https://github.com/BoyBaykiller/meshoptimizer");
        }

        Stopwatch sw = Stopwatch.StartNew();
        Model model = GltfToEngineFormat(gltf, rootTransform, optimizationSettings, usesExtBc5NormalMetallicRoughness, Path.GetFileName(path));
        sw.Stop();

        long totalIndicesCount = 0;
        for (int i = 0; i < model.GpuModel.DrawCommands.Length; i++)
        {
            ref readonly BBG.DrawElementsIndirectCommand cmd = ref model.GpuModel.DrawCommands[i];
            totalIndicesCount += cmd.IndexCount * cmd.InstanceCount;
        }
        Logger.Log(Logger.LogLevel.Info, $"Loaded \"{fileName}\" in {sw.ElapsedMilliseconds}ms (Triangles = {totalIndicesCount / 3})");

        return model;
    }

    private static Model GltfToEngineFormat(ModelRoot gltf, in Matrix4 rootTransform, OptimizationSettings optimizationSettings, bool useExtBc5NormalMetallicRoughness, string modelName)
    {
        (CpuMaterial[] cpuMaterials, GpuMaterial[] gpuMaterials) = LoadMaterials(GetMaterialDescFromGltf(gltf.LogicalMaterials), useExtBc5NormalMetallicRoughness);
        Dictionary<MeshPrimitiveDesc, MeshGeometry> meshPrimitivesGeometry = LoadMeshPrimitivesGeometry(gltf, optimizationSettings);
        List<GpuMesh> listMeshes = new List<GpuMesh>();
        List<GpuMeshInstance> listMeshInstances = new List<GpuMeshInstance>();
        List<BBG.DrawElementsIndirectCommand> listDrawCommands = new List<BBG.DrawElementsIndirectCommand>();
        List<GpuVertex> listVertices = new List<GpuVertex>();
        List<Vector3> listVertexPositions = new List<Vector3>();
        List<uint> listIndices = new List<uint>();
        List<GpuMeshlet> listMeshlets = new List<GpuMeshlet>();
        List<GpuMeshletInfo> listMeshletsInfo = new List<GpuMeshletInfo>();
        List<uint> listMeshletsVertexIndices = new List<uint>();
        List<byte> listMeshletsLocalIndices = new List<byte>();
        List<Vector4i> listJointIndices = new List<Vector4i>();
        List<Vector4> listJointWeights = new List<Vector4>();

        int nodeCounter = 0;

        Node myRoot = new Node();
        myRoot.Name = modelName;
        myRoot.LocalTransform = Transformation.FromMatrix(rootTransform);
        myRoot.ArrayIndex = nodeCounter++;
        myRoot.UpdateGlobalTransform();

        Stack<ValueTuple<GltfNode, Node>> nodeStack = new Stack<ValueTuple<GltfNode, Node>>();
        {
            GltfNode[] gltfChildren = gltf.DefaultScene.VisualChildren.ToArray();
            myRoot.Children = new Node[gltfChildren.Length];
            for (int i = 0; i < gltfChildren.Length; i++)
            {
                GltfNode gltfNode = gltfChildren[i];

                Node myNode = (myRoot.Children[i] = new Node());
                myNode.Parent = myRoot;
                myNode.Name = gltfNode.Name ?? $"RootNode_{i}";

                nodeStack.Push((gltfNode, myNode));
            }
        }

        int jointsCount = 0;
        Dictionary<GltfNode, Node> gltfNodeToMyNode = new Dictionary<GltfNode, Node>(gltf.LogicalNodes.Count);
        while (nodeStack.Count > 0)
        {
            (GltfNode gltfNode, Node myNode) = nodeStack.Pop();

            myNode.ArrayIndex = nodeCounter++;
            myNode.LocalTransform = Transformation.FromMatrix(gltfNode.LocalMatrix.ToOpenTK());
            myNode.UpdateGlobalTransform();

            gltfNodeToMyNode[gltfNode] = myNode;

            {
                GltfNode[] gltfChildren = gltfNode.VisualChildren.ToArray();
                myNode.Children = new Node[gltfChildren.Length];
                for (int i = 0; i < gltfChildren.Length; i++)
                {
                    GltfNode gltfChild = gltfChildren[i];

                    Node myChild = new Node();
                    myChild.Parent = myNode;
                    myChild.Name = gltfChild.Name ?? $"ChildNode_{i}";
                    myNode.Children[i] = myChild;

                    nodeStack.Push((gltfChild, myChild));
                }
            }

            Mesh gltfMesh = gltfNode.Mesh;
            if (gltfMesh == null)
            {
                continue;
            }

            Matrix4[] nodeTransformations = GetNodeTransformations(myNode, gltfNode.UseGpuInstancing());

            Range meshInstanceRange = new Range();
            meshInstanceRange.Start = listMeshInstances.Count;

            for (int i = 0; i < gltfMesh.Primitives.Count; i++)
            {
                MeshPrimitive gltfMeshPrimitive = gltfMesh.Primitives[i];
                if (!meshPrimitivesGeometry.TryGetValue(GetMeshPrimitiveDesc(gltfMeshPrimitive), out MeshGeometry meshGeometry))
                {
                    // MeshPrimitive was not loaded for some reason
                    continue;
                }

                GpuMesh mesh = new GpuMesh();
                mesh.InstanceCount = nodeTransformations.Length;
                mesh.EmissiveBias = 0.0f;
                mesh.SpecularBias = 0.0f;
                mesh.RoughnessBias = 0.0f;
                mesh.TransmissionBias = 0.0f;
                mesh.MeshletsOffset = listMeshlets.Count;
                mesh.MeshletCount = meshGeometry.Meshlets.Length;
                mesh.IORBias = 0.0f;
                mesh.AbsorbanceBias = new Vector3(0.0f);
                mesh.TintOnTransmissive = true; // Required by KHR_materials_transmission
                if (gltfMeshPrimitive.Material == null)
                {
                    // load some default material if mesh primitive doesn't have one
                    (CpuMaterial[] cpuMaterial, GpuMaterial[] gpuMaterial) = LoadMaterials([new MaterialDesc()]);
                    Helper.ArrayAdd(ref cpuMaterials, cpuMaterial[0]);
                    Helper.ArrayAdd(ref gpuMaterials, gpuMaterial[0]);

                    mesh.NormalMapStrength = 0.0f;
                    mesh.MaterialId = gpuMaterial.Length - 1;
                }
                else
                {
                    bool normalMapProvided = !cpuMaterials[gltfMeshPrimitive.Material.LogicalIndex].HasFallbackPixels(TextureType.Normal);
                    mesh.NormalMapStrength = normalMapProvided ? 1.0f : 0.0f;
                    mesh.MaterialId = gltfMeshPrimitive.Material.LogicalIndex;
                }

                GpuMeshInstance[] meshInstances = new GpuMeshInstance[mesh.InstanceCount];
                for (int j = 0; j < meshInstances.Length; j++)
                {
                    ref GpuMeshInstance meshInstance = ref meshInstances[j];

                    // fix for small_city.glb which has a couple malformed transformations
                    //if (nodeTransformations[j].Row1 == new Vector4(0.0f))
                    //{
                    //    nodeTransformations[j].Row1 = Vector4.UnitY;
                    //}

                    meshInstance.ModelMatrix = nodeTransformations[j] * myNode.Parent.GlobalTransform;
                    meshInstance.MeshId = listMeshes.Count;
                }

                BBG.DrawElementsIndirectCommand drawCmd = new BBG.DrawElementsIndirectCommand();
                drawCmd.IndexCount = meshGeometry.VertexIndices.Length;
                drawCmd.InstanceCount = meshInstances.Length;
                drawCmd.FirstIndex = listIndices.Count;
                drawCmd.BaseVertex = listVertices.Count;
                drawCmd.BaseInstance = listMeshInstances.Count;

                listVertices.AddRange(meshGeometry.VertexData.Vertices);
                listVertexPositions.AddRange(meshGeometry.VertexData.Positons);
                listIndices.AddRange(meshGeometry.VertexIndices);
                listMeshes.Add(mesh);
                listMeshInstances.AddRange(meshInstances);
                listDrawCommands.Add(drawCmd);
                listMeshlets.AddRange(meshGeometry.Meshlets);
                listMeshletsInfo.AddRange(meshGeometry.MeshletsInfo);
                listMeshletsVertexIndices.AddRange(new ReadOnlySpan<uint>(meshGeometry.MeshletData.VertexIndices, 0, meshGeometry.MeshletData.VertexIndicesLength));
                listMeshletsLocalIndices.AddRange(new ReadOnlySpan<byte>(meshGeometry.MeshletData.LocalIndices, 0, meshGeometry.MeshletData.LocalIndicesLength));
                listJointIndices.AddRange(meshGeometry.VertexData.JointIndices);
                listJointWeights.AddRange(meshGeometry.VertexData.JointWeights);

                int prevCount = listMeshlets.Count - meshGeometry.Meshlets.Length;
                for (int j = prevCount; j < listMeshlets.Count; j++)
                {
                    GpuMeshlet myMeshlet = listMeshlets[j];

                    // These overflow on big models
                    myMeshlet.VertexOffset += (uint)(listMeshletsVertexIndices.Count - meshGeometry.MeshletData.VertexIndicesLength);
                    myMeshlet.IndicesOffset += (uint)(listMeshletsLocalIndices.Count - meshGeometry.MeshletData.LocalIndicesLength);

                    listMeshlets[j] = myMeshlet;
                }
            }

            jointsCount += gltfNode.Skin != null ? gltfNode.Skin.JointsCount : 0;

            meshInstanceRange.End = listMeshInstances.Count;
            myNode.MeshInstanceRange = meshInstanceRange;
        }

        GpuModel gpuModel = new GpuModel();
        gpuModel.Meshes = listMeshes.ToArray();
        gpuModel.MeshInstances = listMeshInstances.ToArray();
        gpuModel.Materials = gpuMaterials;
        gpuModel.DrawCommands = listDrawCommands.ToArray();
        gpuModel.Vertices = listVertices.ToArray();
        gpuModel.VertexPositions = listVertexPositions.ToArray();
        gpuModel.VertexIndices = listIndices.ToArray();
        gpuModel.Meshlets = listMeshlets.ToArray();
        gpuModel.MeshletsInfo = listMeshletsInfo.ToArray();
        gpuModel.MeshletsVertexIndices = listMeshletsVertexIndices.ToArray();
        gpuModel.MeshletsLocalIndices = listMeshletsLocalIndices.ToArray();
        gpuModel.JointIndices = listJointIndices.ToArray();
        gpuModel.JointWeights = listJointWeights.ToArray();

        Node[] myNodes = new Node[gltf.LogicalNodes.Count + 1];
        Node.HierarchyToArray(myRoot, myNodes);

        LoadNodeSkins(gltf.LogicalNodes, gltfNodeToMyNode);
        ModelAnimation[] animations = LoadAnimations(gltf, gltfNodeToMyNode);

        Model model = new Model();
        model.Nodes = myNodes;
        model.GpuModel = gpuModel;
        model.Animations = animations;
        model.Materials = cpuMaterials;

        return model;
    }

    private static ValueTuple<CpuMaterial[], GpuMaterial[]> LoadMaterials(ReadOnlySpan<MaterialDesc> materialsLoadData, bool useExtBc5NormalMetallicRoughness = false)
    {
        Dictionary<SampledImage, BindlessSampledTexture> uniqueBindlessTextures = new Dictionary<SampledImage, BindlessSampledTexture>();

        CpuMaterial[] cpuMaterials = new CpuMaterial[materialsLoadData.Length];
        GpuMaterial[] gpuMaterials = new GpuMaterial[materialsLoadData.Length];
        for (int i = 0; i < cpuMaterials.Length; i++)
        {
            ref readonly MaterialDesc materialDesc = ref materialsLoadData[i];
            ref readonly MaterialParams materialParams = ref materialDesc.MaterialParams;

            GpuMaterial gpuMaterial = new GpuMaterial();
            gpuMaterial.EmissiveFactor = materialParams.EmissiveFactor;
            gpuMaterial.BaseColorFactor = Compression.CompressUR8G8B8A8(materialParams.BaseColorFactor);
            gpuMaterial.TransmissionFactor = materialParams.TransmissionFactor;
            gpuMaterial.AlphaCutoff = materialParams.AlphaCutoff;
            gpuMaterial.RoughnessFactor = materialParams.RoughnessFactor;
            gpuMaterial.MetallicFactor = materialParams.MetallicFactor;
            gpuMaterial.Absorbance = materialParams.Absorbance;
            gpuMaterial.IOR = materialParams.IOR;
            gpuMaterial.IsVolumetric = materialParams.IsVolumetric;

            if (materialParams.AlphaMode == AlphaMode.Opaque)
            {
                gpuMaterial.AlphaCutoff = 0.0f;
            }
            else if (materialParams.AlphaMode == AlphaMode.Blend)
            {
                // Keep in sync between shader and client code!
                const float valueMeaniningBlendMode = 2.0f;

                gpuMaterial.AlphaCutoff = valueMeaniningBlendMode;
            }

            CpuMaterial cpuMaterial = new CpuMaterial();
            cpuMaterial.Name = materialDesc.Name ?? $"Material_{i}";

            for (int j = 0; j < CpuMaterial.TEXTURE_COUNT; j++)
            {
                TextureType textureType = (TextureType)j;
                SampledImage sampledImage = materialDesc.SampledImages[j];

                bool hasFallbackPixels = false;
                BindlessSampledTexture bindlessTexture;
                if (!sampledImage.HasImage)
                {
                    // By having a pure white fallback we can keep the sampling logic
                    // in shaders the same and still comply to glTF spec
                    bindlessTexture = FallbackTextures.GetWhite();
                    hasFallbackPixels = true;
                }
                else if (!uniqueBindlessTextures.TryGetValue(sampledImage, out bindlessTexture))
                {
                    if (LoadGLTextureAsync(sampledImage, textureType, useExtBc5NormalMetallicRoughness, out bindlessTexture))
                    {
                        uniqueBindlessTextures[sampledImage] = bindlessTexture;
                    }
                    else
                    {
                        if (textureType == TextureType.BaseColor)
                        {
                            bindlessTexture = FallbackTextures.GetPurpleBlack();
                        }
                        else
                        {
                            bindlessTexture = FallbackTextures.GetWhite();
                        }
                        hasFallbackPixels = true;
                    }
                }

                cpuMaterial.SetHasFallbackPixels(textureType, hasFallbackPixels);

                cpuMaterial.SampledTextures[j] = bindlessTexture.SampledTexture;
                gpuMaterial[(GpuMaterial.TextureType)textureType] = bindlessTexture.BindlessHandle;
            }

            cpuMaterials[i] = cpuMaterial;
            gpuMaterials[i] = gpuMaterial;
        }

        return (cpuMaterials, gpuMaterials);
    }

    private static bool LoadGLTextureAsync(SampledImage sampledImage, TextureType textureType, bool useExtBc5NormalMetallicRoughness, out BindlessSampledTexture bindlessTexture)
    {
        bindlessTexture = new BindlessSampledTexture();

        Image gltfImage = sampledImage.GltfImage;

        GLTexture.InternalFormat internalFormat;
        int width, height, levels;
        if (gltfImage.Content.IsPng || gltfImage.Content.IsJpg)
        {
            if (!ImageLoader.TryGetImageHeader(gltfImage.Content.Content.Span, out ImageLoader.ImageHeader imageHeader))
            {
                Logger.Log(Logger.LogLevel.Error, $"Error parsing header of image \"{gltfImage.Name}\"");
                return false;
            }

            internalFormat = textureType switch
            {
                TextureType.BaseColor => GLTexture.InternalFormat.R8G8B8A8SRgb,
                TextureType.MetallicRoughness => GLTexture.InternalFormat.R11G11B10Float,
                TextureType.Normal => GLTexture.InternalFormat.R8G8Unorm,
                TextureType.Emissive => GLTexture.InternalFormat.R8G8B8A8SRgb,
                TextureType.Transmission => GLTexture.InternalFormat.R8Unorm,
                _ => throw new NotSupportedException($"{nameof(textureType)} = {textureType} not supported")
            };

            width = imageHeader.Width;
            height = imageHeader.Height;
            levels = GLTexture.GetMaxMipmapLevel(imageHeader.Width, height, 1);
        }
        else
        {
            Ktx2.Header header = MemoryMarshal.Cast<byte, Ktx2.Header>(gltfImage.Content.Content.Span)[0];
            Ktx2.ErrorCode errorCode = Ktx2.CheckHeader(ref header, out _);
            if (errorCode != Ktx2.ErrorCode.Success)
            {
                Logger.Log(Logger.LogLevel.Error, $"Invalid KTX header \"{gltfImage.Name}\". {nameof(Ktx2.CheckHeader)} returned {errorCode}");
                return false;
            }

            internalFormat = textureType switch
            {
                TextureType.BaseColor => GLTexture.InternalFormat.BC7RgbaSrgb,

                // BC5 support added with gltfpack fork (https://github.com/BoyBaykiller/meshoptimizer) implementing IDK_BC5_normal_metallicRoughness
                TextureType.MetallicRoughness => useExtBc5NormalMetallicRoughness ? GLTexture.InternalFormat.BC5RgUnorm : GLTexture.InternalFormat.BC7RgbaUnorm,
                TextureType.Normal => useExtBc5NormalMetallicRoughness ? GLTexture.InternalFormat.BC5RgUnorm : GLTexture.InternalFormat.BC7RgbaUnorm,

                TextureType.Emissive => GLTexture.InternalFormat.BC7RgbaSrgb,
                TextureType.Transmission => GLTexture.InternalFormat.BC4RUnorm,
                _ => throw new NotSupportedException($"{nameof(textureType)} = {textureType} not supported")
            };

            width = (int)header.PixelWidth;
            height = (int)header.PixelHeight;
            levels = (int)header.LevelCount;
        }

        bool mipmapsRequired = GLSampler.IsMipmapFilter(sampledImage.SamplerState.MinFilter);
        if (!mipmapsRequired)
        {
            levels = 1;
        }

        GLTexture texture = new GLTexture(GLTexture.Type.Texture2D);
        GLSampler sampler = new GLSampler(sampledImage.SamplerState);

        texture.Allocate(width, height, 1, internalFormat, levels);
        if (textureType == TextureType.MetallicRoughness && !useExtBc5NormalMetallicRoughness)
        {
            // By the spec "The metalness values are sampled from the B channel. The roughness values are sampled from the G channel".
            // We move metallic from B into R channel, unless IDK_BC5_normal_metallicRoughness is used where this is already standard behaviour
            texture.SetSwizzleR(GLTexture.Swizzle.B);
        }

        bindlessTexture.SampledTexture.Texture = texture;
        bindlessTexture.SampledTexture.Sampler = sampler;
        bindlessTexture.BindlessHandle = texture.GetTextureHandleARB(sampler);

        MainThreadQueue.AddToLazyQueue(() =>
        {
            /* For uncompressed textures:
             * 1. Create staging buffer on main thread
             * 2. Decode image and copy the pixels into staging buffer in parallel
             * 3. Copy from staging buffer to texture on main thread
             */

            /* For compressed textures:
             * 1. Transcode the KTX texture into GPU compressed format in parallel 
             * 2. Directly upload pixels to texture on main thread
             */

            // TODO: If the main thread is in Sleep State (for example when waiting on Parallel.For() to finish)
            //       it may end up participating as a worker in the ThreadPool.
            //       We want the main thread to only run the render loop only and not some random
            //       ThreadPool work (like loading texturs in this case), because it causes frame stutters

            //System.Threading.ThreadPool.SetMinThreads(Environment.ProcessorCount / 2, 1);
            //System.Threading.ThreadPool.SetMaxThreads(Environment.ProcessorCount / 2, 1);

            if (gltfImage.Content.IsKtx2)
            {
                Task.Run(() =>
                {
                    Ktx2.ErrorCode errCode = Ktx2Texture.FromMemory(gltfImage.Content.Content.Span, Ktx2.TextureCreateFlagBits.LoadImageData | Ktx2.TextureCreateFlagBits.CheckGltfBasisU, out Ktx2Texture ktx2Texture);
                    if (errCode != Ktx2.ErrorCode.Success)
                    {
                        Logger.Log(Logger.LogLevel.Error, $"Failed to load KTX texture. {nameof(Ktx2Texture.FromMemory)} returned {errCode}");
                        return;
                    }

                    errCode = ktx2Texture.Transcode(GLFormatToKtxFormat(texture.Format), Ktx2.TranscodeFlagBits.HighQuality);
                    if (errCode != Ktx2.ErrorCode.Success)
                    {
                        Logger.Log(Logger.LogLevel.Error, $"Failed to transcode KTX texture. {nameof(ktx2Texture.Transcode)} returned {errCode}");
                        return;
                    }

                    MainThreadQueue.AddToLazyQueue(() =>
                    {
                        // We don't own the texture so make sure it didn't get deleted
                        if (!texture.IsDeleted())
                        {
                            for (int level = 0; level < texture.Levels; level++)
                            {
                                Vector3i size = GLTexture.GetMipmapLevelSize(ktx2Texture.BaseWidth, ktx2Texture.BaseHeight, ktx2Texture.BaseDepth, level);
                                ktx2Texture.GetImageDataOffset(level, out nint dataOffset);
                                texture.UploadCompressed2D(size.X, size.Y, ktx2Texture.Data + dataOffset, level);
                            }

                            TextureLoaded?.Invoke();
                        }
                        ktx2Texture.Dispose();
                    });
                });
            }
            else
            {
                ImageLoader.TryGetImageHeader(gltfImage.Content.Content.Span, out ImageLoader.ImageHeader imageHeader);
                ImageLoader.ColorComponents loadComponents = textureType switch
                {
                    TextureType.BaseColor => imageHeader.ColorComponents,
                    TextureType.MetallicRoughness => ImageLoader.ColorComponents.RGB, // MetallicRoughnessTexture stores metalness and roughness in G and B components. Therefore need to load 3 channels :(
                    TextureType.Normal => ImageLoader.ColorComponents.RGB,
                    TextureType.Emissive => ImageLoader.ColorComponents.RGB,
                    TextureType.Transmission => ImageLoader.ColorComponents.R,
                    _ => throw new NotSupportedException($"{nameof(textureType)} = {textureType} not supported")
                };
                imageHeader.SetChannels(loadComponents);

                BBG.TypedBuffer<byte> stagingBuffer = new BBG.TypedBuffer<byte>();
                stagingBuffer.AllocateElements(BBG.Buffer.MemLocation.HostLocal, BBG.Buffer.MemAccess.MappedIncoherent, imageHeader.SizeInBytes);

                Task.Run(() =>
                {
                    {
                        using ImageLoader.ImageResult imageResult = ImageLoader.Load(gltfImage.Content.Content.Span, imageHeader.ColorComponents);
                        if (!imageResult.IsLoadedSuccesfully)
                        {
                            Logger.Log(Logger.LogLevel.Error, $"Image could not be loaded");
                            MainThreadQueue.AddToLazyQueue(stagingBuffer.Dispose);
                            return;
                        }

                        Memory.Copy(imageResult.Memory, stagingBuffer.Memory, imageResult.Header.SizeInBytes);
                    }

                    MainThreadQueue.AddToLazyQueue(() =>
                    {
                        // We don't own the texture so make sure it didn't get deleted
                        if (!texture.IsDeleted())
                        {
                            texture.Upload2D(
                                stagingBuffer,
                                imageHeader.Width, imageHeader.Height,
                                GLTexture.NumChannelsToPixelFormat(imageHeader.Channels),
                                GLTexture.PixelType.UByte,
                                null
                            );
                            texture.GenerateMipmap();

                            TextureLoaded?.Invoke();
                        }
                        stagingBuffer.Dispose();
                    });
                });
            }
        });

        return true;
    }

    private static MaterialDesc[] GetMaterialDescFromGltf(IReadOnlyList<Material> gltfMaterials)
    {
        MaterialDesc[] materialsDesc = new MaterialDesc[gltfMaterials.Count];
        for (int i = 0; i < gltfMaterials.Count; i++)
        {
            Material gltfMaterial = gltfMaterials[i];
            MaterialDesc materialDesc = new MaterialDesc();
            materialDesc.MaterialParams = GetMaterialParams(gltfMaterial);
            materialDesc.Name = gltfMaterial.Name;

            for (int j = 0; j < CpuMaterial.TEXTURE_COUNT; j++)
            {
                TextureType textureType = TextureType.BaseColor + j;
                SampledImage sampledImage = GetSampledImage(gltfMaterial, textureType);

                materialDesc.SampledImages[j] = sampledImage;
            }

            materialsDesc[i] = materialDesc;
            //materialsDesc[i] = new MaterialDesc();
        }

        return materialsDesc;
    }

    private static SampledImage GetSampledImage(Material material, TextureType textureType)
    {
        KnownChannel channel = textureType switch
        {
            TextureType.BaseColor => KnownChannel.BaseColor,
            TextureType.MetallicRoughness => KnownChannel.MetallicRoughness,
            TextureType.Normal => KnownChannel.Normal,
            TextureType.Emissive => KnownChannel.Emissive,
            TextureType.Transmission => KnownChannel.Transmission,
            _ => throw new NotSupportedException($"Can not convert {nameof(textureType)} = {textureType} to {nameof(channel)}"),
        };

        SampledImage sampledImage = new SampledImage();

        MaterialChannel? materialChannel = material.FindChannel(channel.ToString());
        if (materialChannel.HasValue)
        {
            GltfTexture gltfTexture = materialChannel.Value.Texture;
            if (gltfTexture != null)
            {
                sampledImage.GltfImage = gltfTexture.PrimaryImage;
                sampledImage.SamplerState = GetGLSamplerState(gltfTexture.Sampler);
            }
        }

        return sampledImage;
    }

    private static GLSampler.SamplerState GetGLSamplerState(GltfSampler sampler)
    {
        GLSampler.SamplerState state = new GLSampler.SamplerState();
        if (sampler == null)
        {
            state.WrapModeS = GLSampler.WrapMode.Repeat;
            state.WrapModeT = GLSampler.WrapMode.Repeat;
            state.MinFilter = GLSampler.MinFilter.LinearMipmapLinear;
            state.MagFilter = GLSampler.MagFilter.Linear;
        }
        else
        {
            state.WrapModeT = (GLSampler.WrapMode)sampler.WrapT;
            state.WrapModeS = (GLSampler.WrapMode)sampler.WrapS;
            state.MinFilter = (GLSampler.MinFilter)sampler.MinFilter;
            state.MagFilter = (GLSampler.MagFilter)sampler.MagFilter;

            if (sampler.MinFilter == TextureMipMapFilter.DEFAULT)
            {
                state.MinFilter = GLSampler.MinFilter.LinearMipmapLinear;
            }
            if (sampler.MagFilter == TextureInterpolationFilter.DEFAULT)
            {
                state.MagFilter = GLSampler.MagFilter.Linear;
            }
        }

        bool isMipmapFilter = GLSampler.IsMipmapFilter(state.MinFilter);
        state.Anisotropy = isMipmapFilter ? GLSampler.Anisotropy.Samples8x : GLSampler.Anisotropy.Samples1x;

        return state;
    }

    private static Matrix4[] GetNodeTransformations(Node node, MeshGpuInstancing meshGpuInstancing)
    {
        if (meshGpuInstancing.Count == 0)
        {
            // If its not using EXT_mesh_gpu_instancing we must use local transform
            return [node.LocalTransform.GetMatrix()];
        }

        Matrix4[] nodeInstances = new Matrix4[meshGpuInstancing.Count];
        for (int i = 0; i < nodeInstances.Length; i++)
        {
            nodeInstances[i] = meshGpuInstancing.GetLocalMatrix(i).ToOpenTK();
        }

        return nodeInstances;
    }

    private static Dictionary<MeshPrimitiveDesc, MeshGeometry> LoadMeshPrimitivesGeometry(ModelRoot modelRoot, OptimizationSettings optimizationSettings)
    {
        int maxMeshPrimitives = modelRoot.LogicalMeshes.Sum(it => it.Primitives.Count);

        Task[] tasks = new Task[maxMeshPrimitives];
        Dictionary<MeshPrimitiveDesc, MeshGeometry> uniqueMeshPrimitives = new Dictionary<MeshPrimitiveDesc, MeshGeometry>(maxMeshPrimitives);

        int uniqueMeshPrimitivesCount = 0;
        for (int i = 0; i < modelRoot.LogicalMeshes.Count; i++)
        {
            Mesh mesh = modelRoot.LogicalMeshes[i];
            for (int j = 0; j < mesh.Primitives.Count; j++)
            {
                MeshPrimitive meshPrimitive = mesh.Primitives[j];

                MeshPrimitiveDesc meshDesc = GetMeshPrimitiveDesc(meshPrimitive);
                if (meshPrimitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                {
                    Logger.Log(Logger.LogLevel.Error, $"Unsupported {nameof(MeshPrimitive.DrawPrimitiveType)} = {meshPrimitive.DrawPrimitiveType}");
                    continue;
                }

                // TryAdd instead of Contains will prevent entering multiple times 
                // before the task has actually written a Value
                if (uniqueMeshPrimitives.TryAdd(meshDesc, new MeshGeometry()))
                {
                    tasks[uniqueMeshPrimitivesCount++] = Task.Run(() =>
                    {
                        (VertexData meshVertexData, uint[] meshIndices) = LoadVertexAndIndices(modelRoot.LogicalAccessors, meshDesc);
                        OptimizeMesh(ref meshVertexData.Vertices, ref meshVertexData.Positons, meshIndices, optimizationSettings);

                        MeshletData meshletData = GenerateMeshlets(meshVertexData.Positons, meshIndices);
                        (GpuMeshlet[] meshMeshlets, GpuMeshletInfo[] meshMeshletsInfo) = LoadGpuMeshlets(meshletData, meshVertexData.Positons);

                        MeshGeometry meshGeometry = new MeshGeometry();
                        meshGeometry.VertexData = meshVertexData;
                        meshGeometry.VertexIndices = meshIndices;
                        meshGeometry.MeshletData = meshletData;
                        meshGeometry.Meshlets = meshMeshlets;
                        meshGeometry.MeshletsInfo = meshMeshletsInfo;

                        uniqueMeshPrimitives[meshDesc] = meshGeometry;
                    });
                }
            }
        }
        int deduplicatedCount = maxMeshPrimitives - uniqueMeshPrimitivesCount;

        while (uniqueMeshPrimitivesCount < tasks.Length)
        {
            tasks[uniqueMeshPrimitivesCount++] = Task.CompletedTask;
        }

        Task.WaitAll(tasks);

        return uniqueMeshPrimitives;
    }

    private static MeshPrimitiveDesc GetMeshPrimitiveDesc(MeshPrimitive meshPrimitive)
    {
        Accessor positonAccessor = meshPrimitive.VertexAccessors["POSITION"];
        bool hasNormals = meshPrimitive.VertexAccessors.TryGetValue("NORMAL", out Accessor normalAccessor);
        bool hasTexCoords = meshPrimitive.VertexAccessors.TryGetValue("TEXCOORD_0", out Accessor texCoordAccessor);
        bool hasJoints = meshPrimitive.VertexAccessors.TryGetValue("JOINTS_0", out Accessor jointsAccessor);
        bool hasWeights = meshPrimitive.VertexAccessors.TryGetValue("WEIGHTS_0", out Accessor weightsAccessor);
        bool hasIndices = meshPrimitive.IndexAccessor != null;

        MeshPrimitiveDesc meshDesc = new MeshPrimitiveDesc();
        meshDesc.PositionAccessor = positonAccessor.LogicalIndex;
        if (hasNormals) meshDesc.NormalAccessor = normalAccessor.LogicalIndex;
        if (hasTexCoords) meshDesc.TexCoordAccessor = texCoordAccessor.LogicalIndex;
        if (hasJoints) meshDesc.JointsAccessor = jointsAccessor.LogicalIndex;
        if (hasWeights) meshDesc.WeightsAccessor = weightsAccessor.LogicalIndex;
        if (hasIndices) meshDesc.IndexAccessor = meshPrimitive.IndexAccessor.LogicalIndex;

        return meshDesc;
    }

    private static ValueTuple<VertexData, uint[]> LoadVertexAndIndices(IReadOnlyList<Accessor> accessors, in MeshPrimitiveDesc meshDesc)
    {
        Accessor positonAccessor = accessors[meshDesc.PositionAccessor];

        VertexData vertexData = new VertexData();
        vertexData.Vertices = new GpuVertex[positonAccessor.Count];
        vertexData.Positons = new Vector3[positonAccessor.Count];
        vertexData.JointIndices = [];
        vertexData.JointWeights = [];

        IterateAccessor(positonAccessor, (in Vector3 pos, int i) =>
        {
            vertexData.Positons[i] = pos;
        });

        if (meshDesc.HasNormalAccessor)
        {
            Accessor normalAccessor = accessors[meshDesc.NormalAccessor];
            IterateAccessor(normalAccessor, (in Vector3 normal, int i) =>
            {
                vertexData.Vertices[i].Normal = Compression.CompressSR11G11B10(normal);

                Vector3 c1 = Vector3.Cross(normal, Vector3.UnitZ);
                Vector3 c2 = Vector3.Cross(normal, Vector3.UnitY);
                Vector3 tangent = Vector3.Dot(c1, c1) > Vector3.Dot(c2, c2) ? c1 : c2;
                vertexData.Vertices[i].Tangent = Compression.CompressSR11G11B10(tangent);
            });
        }
        else
        {
            Logger.Log(Logger.LogLevel.Error, "Mesh provides no vertex normals");
        }

        if (meshDesc.HasTexCoordAccessor)
        {
            Accessor texCoordAccessor = accessors[meshDesc.TexCoordAccessor];
            if (texCoordAccessor.Encoding == EncodingType.FLOAT)
            {
                IterateAccessor(texCoordAccessor, (in Vector2 texCoord, int i) =>
                {
                    vertexData.Vertices[i].TexCoord = texCoord;
                });
            }
            else
            {
                Logger.Log(Logger.LogLevel.Error, $"Unsupported TexCoord {nameof(texCoordAccessor.Encoding)} = {texCoordAccessor.Encoding}");
            }
        }

        if (meshDesc.HasJointsAccessor)
        {
            Accessor jointsAccessor = accessors[meshDesc.JointsAccessor];
            vertexData.JointIndices = new Vector4i[jointsAccessor.Count];
            if (jointsAccessor.Encoding == EncodingType.UNSIGNED_SHORT)
            {
                IterateAccessor(jointsAccessor, (in USVec4 jointIds, int i) =>
                {
                    vertexData.JointIndices[i] = new Vector4i(jointIds.Data[0], jointIds.Data[1], jointIds.Data[2], jointIds.Data[3]);
                });
            }
            else if (jointsAccessor.Encoding == EncodingType.UNSIGNED_BYTE)
            {
                IterateAccessor(jointsAccessor, (in BVec4 jointIds, int i) =>
                {
                    vertexData.JointIndices[i] = new Vector4i(jointIds.Data[0], jointIds.Data[1], jointIds.Data[2], jointIds.Data[3]);
                });
            }
        }

        if (meshDesc.HasWeightsAccessor)
        {
            Accessor weightsAccessor = accessors[meshDesc.WeightsAccessor];
            vertexData.JointWeights = new Vector4[weightsAccessor.Count];
            if (weightsAccessor.Encoding == EncodingType.FLOAT)
            {
                IterateAccessor(weightsAccessor, (in Vector4 weights, int i) =>
                {
                    vertexData.JointWeights[i] = weights;
                });
            }
            else if (weightsAccessor.Encoding == EncodingType.UNSIGNED_SHORT)
            {
                IterateAccessor(weightsAccessor, (in USVec4 weights, int i) =>
                {
                    vertexData.JointWeights[i] = DecodeNormalizedIntsToFloats(weights);
                });
            }
            else if (weightsAccessor.Encoding == EncodingType.UNSIGNED_BYTE)
            {
                IterateAccessor(weightsAccessor, (in BVec4 weights, int i) =>
                {
                    vertexData.JointWeights[i] = DecodeNormalizedIntsToFloats(weights);
                });
            }
        }

        uint[] vertexIndices = null;
        if (meshDesc.HasIndexAccessor)
        {
            Accessor accessor = accessors[meshDesc.IndexAccessor];
            vertexIndices = new uint[accessor.Count];
            IterateAccessor(accessor, (in uint index, int i) =>
            {
                vertexIndices[i] = index;
            });
        }
        else
        {
            vertexIndices = new uint[positonAccessor.Count];
            Helper.FillIncreasing(vertexIndices);
        }

        return (vertexData, vertexIndices);
    }

    private static ValueTuple<GpuMeshlet[], GpuMeshletInfo[]> LoadGpuMeshlets(in MeshletData meshletsData, ReadOnlySpan<Vector3> meshVertexPositions)
    {
        GpuMeshlet[] gpuMeshlets = new GpuMeshlet[meshletsData.MeshletsLength];
        GpuMeshletInfo[] gpuMeshletsInfo = new GpuMeshletInfo[gpuMeshlets.Length];
        for (int i = 0; i < gpuMeshlets.Length; i++)
        {
            ref GpuMeshlet meshlet = ref gpuMeshlets[i];
            ref GpuMeshletInfo meshletInfo = ref gpuMeshletsInfo[i];
            ref readonly Meshopt.Meshlet meshOptMeshlet = ref meshletsData.Meshlets[i];

            meshlet.VertexOffset = meshOptMeshlet.VertexOffset;
            meshlet.VertexCount = (byte)meshOptMeshlet.VertexCount;
            meshlet.IndicesOffset = meshOptMeshlet.TriangleOffset;
            meshlet.TriangleCount = (byte)meshOptMeshlet.TriangleCount;

            Box meshletBoundingBox = Box.Empty();
            for (uint j = meshlet.VertexOffset; j < meshlet.VertexOffset + meshlet.VertexCount; j++)
            {
                uint vertexIndex = meshletsData.VertexIndices[j];
                meshletBoundingBox.GrowToFit(meshVertexPositions[(int)vertexIndex]);
            }
            meshletInfo.Min = meshletBoundingBox.Min;
            meshletInfo.Max = meshletBoundingBox.Max;
        }

        return (gpuMeshlets, gpuMeshletsInfo);
    }

    private static void LoadNodeSkins(IReadOnlyList<GltfNode> gltfNodes, Dictionary<GltfNode, Node> gltfNodeToMyNode)
    {
        for (int i = 0; i < gltfNodes.Count; i++)
        {
            GltfNode gltfNode = gltfNodes[i];
            if (gltfNode.Skin != null)
            {
                Node myNode = gltfNodeToMyNode[gltfNode];
                myNode.Skin = new Skin();
                myNode.Skin.Joints = new Node[gltfNode.Skin.JointsCount];
                myNode.Skin.InverseJointMatrices = new Matrix4[myNode.Skin.JointsCount];

                for (int j = 0; j < myNode.Skin.JointsCount; j++)
                {
                    (GltfNode gltfJoint, System.Numerics.Matrix4x4 inverseJointMatrix) = gltfNode.Skin.GetJoint(j);

                    myNode.Skin.Joints[j] = gltfNodeToMyNode[gltfJoint];
                    myNode.Skin.InverseJointMatrices[j] = inverseJointMatrix.ToOpenTK();
                }
            }
        }
    }

    private static ModelAnimation[] LoadAnimations(ModelRoot gltf, Dictionary<GltfNode, Node> gltfNodeToMyNode)
    {
        int animationCount = 0;
        ModelAnimation[] animations = new ModelAnimation[gltf.LogicalAnimations.Count];
        for (int i = 0; i < gltf.LogicalAnimations.Count; i++)
        {
            GltfAnimation gltfAnimation = gltf.LogicalAnimations[i];

            ModelAnimation myAnimation = new ModelAnimation();
            myAnimation.Name = gltfAnimation.Name ?? $"Animation_{i}";

            int nodeAmimsCount = 0;
            myAnimation.NodeAnimations = new NodeAnimation[gltfAnimation.Channels.Count];
            for (int j = 0; j < gltfAnimation.Channels.Count; j++)
            {
                AnimationChannel animationChannel = gltfAnimation.Channels[j];
                if (TryGetNodeAnimation(animationChannel, gltfNodeToMyNode, out NodeAnimation nodeAnimation))
                {
                    myAnimation.NodeAnimations[nodeAmimsCount++] = nodeAnimation;
                }
            }

            if (nodeAmimsCount > 0)
            {
                Array.Resize(ref myAnimation.NodeAnimations, nodeAmimsCount);

                myAnimation.Start = myAnimation.NodeAnimations.Select(it => it.Start).Min();
                myAnimation.End = myAnimation.NodeAnimations.Select(it => it.End).Max();
                animations[animationCount++] = myAnimation;
            }
        }
        Array.Resize(ref animations, animationCount);

        return animations;
    }

    private static bool TryGetNodeAnimation(AnimationChannel animationChannel, Dictionary<GltfNode, Node> gltfNodeToMyNode, out NodeAnimation animation)
    {
        animation = new NodeAnimation();
        animation.TargetNode = gltfNodeToMyNode[animationChannel.TargetNode];
        animation.Type = (NodeAnimation.AnimationType)animationChannel.TargetNodePath;

        if (animationChannel.TargetNodePath == PropertyPath.scale ||
            animationChannel.TargetNodePath == PropertyPath.translation)
        {
            IAnimationSampler<System.Numerics.Vector3> gltfAnimationSampler =
                animationChannel.TargetNodePath == PropertyPath.scale ?
                animationChannel.GetScaleSampler() :
                animationChannel.GetTranslationSampler();
            animation.Mode = (NodeAnimation.InterpolationMode)gltfAnimationSampler.InterpolationMode;

            ValueTuple<float, System.Numerics.Vector3>[] keys = null;
            if (animation.Mode == NodeAnimation.InterpolationMode.Step ||
                animation.Mode == NodeAnimation.InterpolationMode.Linear)
            {
                keys = gltfAnimationSampler.GetLinearKeys().ToArray();
            }
            else if (gltfAnimationSampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
            {
                Logger.Log(Logger.LogLevel.Error, $"Unsupported {nameof(gltfAnimationSampler.InterpolationMode)} = {gltfAnimationSampler.InterpolationMode}");
                return false;
            }

            animation.KeyFramesStart = new float[keys.Length];
            animation.RawKeyFramesData = new byte[sizeof(Vector3) * keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                (float time, System.Numerics.Vector3 value) = keys[i];
                animation.KeyFramesStart[i] = time;
                animation.GetKeyFrameDataAsVec3()[i] = value.ToOpenTK();
            }
        }
        else if (animationChannel.TargetNodePath == PropertyPath.rotation)
        {
            IAnimationSampler<System.Numerics.Quaternion> gltfAnimationSampler = animationChannel.GetRotationSampler();
            animation.Mode = (NodeAnimation.InterpolationMode)gltfAnimationSampler.InterpolationMode;
            
            ValueTuple<float, System.Numerics.Quaternion>[] keys = null;
            if (animation.Mode == NodeAnimation.InterpolationMode.Step ||
                animation.Mode == NodeAnimation.InterpolationMode.Linear)
            {
                keys = gltfAnimationSampler.GetLinearKeys().ToArray();
            }
            else if (gltfAnimationSampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE)
            {
                Logger.Log(Logger.LogLevel.Error, $"Unsupported {nameof(gltfAnimationSampler.InterpolationMode)} = {gltfAnimationSampler.InterpolationMode}");
                return false;
            }

            animation.KeyFramesStart = new float[keys.Length];
            animation.RawKeyFramesData = new byte[sizeof(Quaternion) * keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                (float time, System.Numerics.Quaternion value) = keys[i];
                animation.KeyFramesStart[i] = time;
                animation.GetKeyFrameDataAsQuaternion()[i] = value.ToOpenTK();
            }
        }
        else
        {
            Logger.Log(Logger.LogLevel.Error, $"Unsupported {nameof(animationChannel.TargetNodePath)} = {animationChannel.TargetNodePath}");
            return false;
        }

        return true;
    }

    private static MaterialParams GetMaterialParams(Material gltfMaterial)
    {
        MaterialParams materialParams = new MaterialParams();

        materialParams.AlphaCutoff = gltfMaterial.AlphaCutoff;
        materialParams.AlphaMode = (AlphaMode)gltfMaterial.Alpha;

        MaterialChannel? baseColorChannel = gltfMaterial.FindChannel(KnownChannel.BaseColor.ToString());
        if (baseColorChannel.HasValue)
        {
            System.Numerics.Vector4 baseColor = GetMaterialChannelParam<System.Numerics.Vector4>(baseColorChannel.Value, KnownProperty.RGBA);
            materialParams.BaseColorFactor = baseColor.ToOpenTK();
        }

        MaterialChannel? metallicRoughnessChannel = gltfMaterial.FindChannel(KnownChannel.MetallicRoughness.ToString());
        if (metallicRoughnessChannel.HasValue)
        {
            materialParams.RoughnessFactor = GetMaterialChannelParam<float>(metallicRoughnessChannel.Value, KnownProperty.RoughnessFactor);
            materialParams.MetallicFactor = GetMaterialChannelParam<float>(metallicRoughnessChannel.Value, KnownProperty.MetallicFactor);
        }

        MaterialChannel? emissiveChannel = gltfMaterial.FindChannel(KnownChannel.Emissive.ToString());
        if (emissiveChannel.HasValue) // KHR_materials_emissive_strength
        {
            float emissiveStrength = GetMaterialChannelParam<float>(emissiveChannel.Value, KnownProperty.EmissiveStrength);

            materialParams.EmissiveFactor = emissiveChannel.Value.Color.ToOpenTK().Xyz * emissiveStrength;
        }

        MaterialChannel? transmissionChannel = gltfMaterial.FindChannel(KnownChannel.Transmission.ToString());
        if (transmissionChannel.HasValue) // KHR_materials_transmission
        {
            materialParams.TransmissionFactor = GetMaterialChannelParam<float>(transmissionChannel.Value, KnownProperty.TransmissionFactor);
        }

        materialParams.IOR = gltfMaterial.IndexOfRefraction; // KHR_materials_ior

        MaterialChannel? volumeAttenuationChannel = gltfMaterial.FindChannel(KnownChannel.VolumeAttenuation.ToString());
        if (volumeAttenuationChannel.HasValue) // KHR_materials_volume
        {
            System.Numerics.Vector3 numericsGltfAttenuationColor = GetMaterialChannelParam<System.Numerics.Vector3>(volumeAttenuationChannel.Value, KnownProperty.RGB);
            Vector3 gltfAttenuationColor = numericsGltfAttenuationColor.ToOpenTK();

            float gltfAttenuationDistance = GetMaterialChannelParam<float>(volumeAttenuationChannel.Value, KnownProperty.AttenuationDistance);

            // We can combine glTF Attenuation Color and Distance into a single Absorbance value
            // Source: https://github.com/DassaultSystemes-Technology/dspbr-pt/blob/e7cfa6e9aab2b99065a90694e1f58564d675c1a4/packages/lib/shader/integrator/pt.glsl#L24
            float x = -MathF.Log(gltfAttenuationColor.X) / gltfAttenuationDistance;
            float y = -MathF.Log(gltfAttenuationColor.Y) / gltfAttenuationDistance;
            float z = -MathF.Log(gltfAttenuationColor.Z) / gltfAttenuationDistance;
            Vector3 absorbance = new Vector3(x, y, z);
            materialParams.Absorbance = absorbance;
        }

        MaterialChannel? volumeThicknesssChannel = gltfMaterial.FindChannel(KnownChannel.VolumeThickness.ToString());
        if (volumeThicknesssChannel.HasValue) // KHR_materials_volume
        {
            float thicknessFactor = GetMaterialChannelParam<float>(volumeThicknesssChannel.Value, KnownProperty.ThicknessFactor);
            materialParams.ThicknessFactor = thicknessFactor;
        }

        return materialParams;
    }

    private static T GetMaterialChannelParam<T>(MaterialChannel materialChannel, KnownProperty property)
    {
        foreach (IMaterialParameter param in materialChannel.Parameters)
        {
            if (param.Name == property.ToString())
            {
                return (T)param.Value;
            }
        }

        throw new UnreachableException($"{nameof(property)} = {property} is not a part of the {nameof(materialChannel)}");
    }

    private static Ktx2.TranscodeFormat GLFormatToKtxFormat(GLTexture.InternalFormat internalFormat)
    {
        switch (internalFormat)
        {
            case GLTexture.InternalFormat.BC1RgbUnorm:
                return Ktx2.TranscodeFormat.BC1Rgb;

            case GLTexture.InternalFormat.BC4RUnorm:
                return Ktx2.TranscodeFormat.BC4R;

            case GLTexture.InternalFormat.BC5RgUnorm:
                return Ktx2.TranscodeFormat.BC5Rg;

            case GLTexture.InternalFormat.BC7RgbaUnorm:
            case GLTexture.InternalFormat.BC7RgbaSrgb:
                return Ktx2.TranscodeFormat.BC7Rgba;

            case GLTexture.InternalFormat.Astc4X4RgbaKHR:
            case GLTexture.InternalFormat.Astc4X4RgbaSrgbKHR:
                return Ktx2.TranscodeFormat.Astc4X4Rgba;

            default:
                throw new NotSupportedException($"Can not convert {nameof(internalFormat)} = {internalFormat} to {nameof(Ktx2.TranscodeFormat)}");
        }
    }

    private static int EncodingToSize(EncodingType encodingType)
    {
        int size = encodingType switch
        {
            EncodingType.UNSIGNED_BYTE or EncodingType.BYTE => 1,
            EncodingType.UNSIGNED_SHORT or EncodingType.SHORT => 2,
            EncodingType.FLOAT or EncodingType.UNSIGNED_INT => 4,
            _ => throw new NotSupportedException($"Can not convert {nameof(encodingType)} = {encodingType} to {nameof(size)}"),
        };
        return size;
    }

    private static int DimensionsToNum(DimensionType dimensionType)
    {
        int num = dimensionType switch
        {
            DimensionType.SCALAR => 1,
            DimensionType.VEC2 => 2,
            DimensionType.VEC3 => 3,
            DimensionType.VEC4 => 4,
            DimensionType.MAT4 => 16,
            _ => throw new NotSupportedException($"Can not convert {nameof(dimensionType)} = {dimensionType} to {nameof(num)}"),
        };
        return num;
    }

    private static void OptimizeMesh(ref GpuVertex[] meshVertices, ref Vector3[] meshVertexPositions, Span<uint> meshIndices, OptimizationSettings optimizationSettings)
    {
        if (optimizationSettings.VertexRemapOptimization)
        {
            uint[] remapTable = new uint[meshVertices.Length];
            fixed (void* meshVerticesPtr = meshVertices, meshPositionsPtr = meshVertexPositions)
            {
                Span<Meshopt.Stream> vertexStreams = stackalloc Meshopt.Stream[2];
                vertexStreams[0] = new Meshopt.Stream() { Data = meshVerticesPtr, Size = (nuint)sizeof(GpuVertex), Stride = (nuint)sizeof(GpuVertex) };
                vertexStreams[1] = new Meshopt.Stream() { Data = meshPositionsPtr, Size = (nuint)sizeof(Vector3), Stride = (nuint)sizeof(Vector3) };

                int optimizedVertexCount = (int)Meshopt.GenerateVertexRemapMulti(ref remapTable[0], meshIndices[0], (nuint)meshIndices.Length, (nuint)meshVertices.Length, vertexStreams[0], (nuint)vertexStreams.Length);

                Meshopt.RemapIndexBuffer(ref meshIndices[0], meshIndices[0], (nuint)meshIndices.Length, remapTable[0]);
                Meshopt.RemapVertexBuffer(vertexStreams[0].Data, vertexStreams[0].Data, (nuint)meshVertices.Length, vertexStreams[0].Stride, remapTable[0]);
                Meshopt.RemapVertexBuffer(vertexStreams[1].Data, vertexStreams[1].Data, (nuint)meshVertexPositions.Length, vertexStreams[1].Stride, remapTable[0]);

                Array.Resize(ref meshVertices, optimizedVertexCount);
                Array.Resize(ref meshVertexPositions, optimizedVertexCount);
            }
        }
        if (optimizationSettings.VertexCacheOptimization)
        {
            Meshopt.OptimizeVertexCache(ref meshIndices[0], meshIndices[0], (nuint)meshIndices.Length, (nuint)meshVertices.Length);
        }
        if (optimizationSettings.VertexFetchOptimization)
        {
            uint[] remapTable = new uint[meshVertices.Length];
            fixed (void* meshVerticesPtr = meshVertices, meshPositionsPtr = meshVertexPositions)
            {
                int optimizedVertexCount = (int)Meshopt.OptimizeVertexFetchRemap(ref remapTable[0], meshIndices[0], (nuint)meshIndices.Length, (nuint)meshVertices.Length);

                Meshopt.RemapIndexBuffer(ref meshIndices[0], meshIndices[0], (nuint)meshIndices.Length, remapTable[0]);
                Meshopt.RemapVertexBuffer(meshVerticesPtr, meshVerticesPtr, (nuint)meshVertices.Length, (nuint)sizeof(GpuVertex), remapTable[0]);
                Meshopt.RemapVertexBuffer(meshPositionsPtr, meshPositionsPtr, (nuint)meshVertexPositions.Length, (nuint)sizeof(Vector3), remapTable[0]);

                Array.Resize(ref meshVertices, optimizedVertexCount);
                Array.Resize(ref meshVertexPositions, optimizedVertexCount);
            }
        }
    }

    private static MeshletData GenerateMeshlets(ReadOnlySpan<Vector3> meshVertexPositions, ReadOnlySpan<uint> meshIndices)
    {
        const float CONE_WEIGHT = 0.0f;

        /// Keep in sync between shader and client code!
        // perfectly fits 4 32-sized subgroups
        const uint MESHLET_MAX_VERTEX_COUNT = 128;

        // (252 * 3) + 4(hardware reserved) = 760bytes. Which almost perfectly fits NVIDIA-Turing 128 byte allocation granularity.
        // Meshoptimizer also requires this to be divisible by 4
        const uint MESHLET_MAX_TRIANGLE_COUNT = 252;

        nuint maxMeshlets = Meshopt.BuildMeshletsBound((nuint)meshIndices.Length, MESHLET_MAX_VERTEX_COUNT, MESHLET_MAX_TRIANGLE_COUNT);

        Meshopt.Meshlet[] meshlets = new Meshopt.Meshlet[maxMeshlets];
        uint[] meshletsVertexIndices = new uint[maxMeshlets * MESHLET_MAX_VERTEX_COUNT];
        byte[] meshletsLocalIndices = new byte[maxMeshlets * MESHLET_MAX_TRIANGLE_COUNT * 3];
        nuint meshletCount = Meshopt.BuildMeshlets(
            ref meshlets[0],
            meshletsVertexIndices[0],
            meshletsLocalIndices[0],
            meshIndices[0],
            (nuint)meshIndices.Length,
            meshVertexPositions[0].X,
            (nuint)meshVertexPositions.Length,
            (nuint)sizeof(Vector3),
            MESHLET_MAX_VERTEX_COUNT,
            MESHLET_MAX_TRIANGLE_COUNT,
            CONE_WEIGHT
        );

        byte[] meshletsLocalIndicesPacked = new byte[maxMeshlets * (MESHLET_MAX_TRIANGLE_COUNT * 3 + 3)];
        uint meshletsLocalIndicesPackedOffset = 0;

        for (int i = 0; i < meshlets.Length; i++)
        {
            ref Meshopt.Meshlet meshlet = ref meshlets[i];

            // https://zeux.io/2024/04/09/meshlet-triangle-locality/
            Meshopt.OptimizeMeshlet(
                ref meshletsVertexIndices[meshlet.VertexOffset],
                ref meshletsLocalIndices[meshlet.TriangleOffset],
                meshlet.TriangleCount,
                meshlet.VertexCount
            );

            // Repack meshlets to be aligned to 4 bytes (to be able to use 32-bit loads and writePackedPrimitiveIndices4x8NV)
            Array.Copy(meshletsLocalIndices, meshlet.TriangleOffset, meshletsLocalIndicesPacked, meshletsLocalIndicesPackedOffset, meshlet.TriangleCount * 3);
            meshlet.TriangleOffset = meshletsLocalIndicesPackedOffset;
            meshletsLocalIndicesPackedOffset += MyMath.AlignUp(meshlet.TriangleCount * 3, 4);
        }

        ref readonly Meshopt.Meshlet last = ref meshlets[meshletCount - 1];
        uint meshletsVertexIndicesLength = last.VertexOffset + last.VertexCount;
        uint meshletsLocalIndicesLength = last.TriangleOffset + MyMath.AlignUp(last.TriangleCount * 3, 4);

        MeshletData result;
        result.Meshlets = meshlets;
        result.MeshletsLength = (int)meshletCount;

        result.VertexIndices = meshletsVertexIndices;
        result.VertexIndicesLength = (int)meshletsVertexIndicesLength;

        Debug.Assert(meshletsLocalIndicesLength == meshletsLocalIndicesPackedOffset);
        result.LocalIndices = meshletsLocalIndicesPacked;
        result.LocalIndicesLength = (int)meshletsLocalIndicesLength;

        return result;
    }

    private delegate void FuncAccessorItem<T>(in T item, int index);
    private static void IterateAccessor<T>(Accessor accessor, FuncAccessorItem<T> funcItem) where T : unmanaged
    {
        if (accessor.IsSparse)
        {
            throw new ArgumentException("Sparse accessor is not supported");
        }

        int itemSize = EncodingToSize(accessor.Encoding) * DimensionsToNum(accessor.Dimensions);
        int stride = accessor.SourceBufferView.ByteStride == 0 ? itemSize : accessor.SourceBufferView.ByteStride;

        if (sizeof(T) < itemSize)
        {
            throw new ArgumentException($"{nameof(T)} is smaller than a single item in the accessor ({nameof(itemSize)} = {itemSize})");
        }

        Span<byte> data = accessor.SourceBufferView.Content.AsSpan(accessor.ByteOffset, accessor.ByteLength);
        fixed (byte* ptr = data)
        {
            for (int i = 0; i < accessor.Count; i++)
            {
                T t = new T();
                byte* head = ptr + i * stride;
                Memory.Copy(head, &t, itemSize);

                funcItem(t, i);
            }
        }
    }

    private static Vector4 DecodeNormalizedIntsToFloats(in BVec4 bVec4)
    {
        return new Vector4(bVec4.Data[0] / 255.0f, bVec4.Data[1] / 255.0f, bVec4.Data[2] / 255.0f, bVec4.Data[3] / 255.0f);
    }

    private static Vector4 DecodeNormalizedIntsToFloats(in USVec4 usvec4)
    {
        return new Vector4(usvec4.Data[0] / 65535.0f, usvec4.Data[1] / 65535.0f, usvec4.Data[2] / 65535.0f, usvec4.Data[3] / 65535.0f);
    }

    public static class GtlfpackWrapper
    {
        public const string CLI_NAME = "gltfpack"; // https://github.com/BoyBaykiller/meshoptimizer
        public static readonly string? CliPath = TryFindGltfpack();

        public static bool CliFound => CliPath != null;

        public record struct GltfpackSettings
        {
            public string InputPath;
            public string OutputPath;
            public int ThreadsUsed;
            public bool UseInstancing;

            // Added in gltfpack fork
            public bool KeepMeshPrimitives;

            public Action<string>? ProcessError;
            public Action<string>? ProcessOutput;
        }

        public static Task Run(GltfpackSettings settings)
        {
            if (!CliFound)
            {
                Logger.Log(Logger.LogLevel.Error, $"Can't run {CLI_NAME}. Tool is not found");
                return null;
            }

            // -v         = verbose output
            // -noq       = no mesh quantization (KHR_mesh_quantization)
            // -ac        = keep constant animation tracks even if they don't modify the node transform
            // -tc        = do KTX2 texture compression (KHR_texture_basisu)
            // -tq        = texture quality
            // -mi        = use instancing (EXT_mesh_gpu_instancing)
            // -kp        = disable mesh primitive merging (added in gltfpack fork)
            // -tj        = number of threads to use when compressing textures
            string arguments = $"-v -noq -ac -tc -tq 10 " +
                               $"{MaybeArgument("-mi", settings.UseInstancing)} " +
                               $"{MaybeArgument("-kp", settings.KeepMeshPrimitives)} " +
                               $"-tj {settings.ThreadsUsed} " +
                               $"-i {settings.InputPath} -o {settings.OutputPath}";

            ProcessStartInfo startInfo = new ProcessStartInfo()
            {
                FileName = CliPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Arguments = arguments,
            };

            try
            {
                Logger.Log(Logger.LogLevel.Info, $"Running \"{CLI_NAME} {arguments}\"");

                Process proc = Process.Start(startInfo);

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();
                proc.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }

                    settings.ProcessError?.Invoke($"{CLI_NAME}: {e.Data}");
                };
                proc.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null)
                    {
                        return;
                    }

                    settings.ProcessOutput?.Invoke($"{CLI_NAME}: {e.Data}");
                };

                return proc.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                Logger.Log(Logger.LogLevel.Error, $"Failed to create process. {ex}");
                return null;
            }

            static string MaybeArgument(string argument, bool yes)
            {
                if (yes)
                {
                    return argument;
                }

                return string.Empty;
            }
        }

        public static bool IsCompressed(ModelRoot gltf)
        {
            if (gltf.LogicalTextures.Count == 0)
            {
                return true;
            }

            foreach (string ext in gltf.ExtensionsUsed)
            {
                // The definition of wether a glTF is compressed may be expanded in the future
                if (ext == "KHR_texture_basisu")
                {
                    return true;
                }
            }
            return false;
        }

        private static string? TryFindGltfpack()
        {
            List<string> pathsToSearch = [Directory.GetCurrentDirectory()];
            {
                if (TryGetEnvironmentVariable("PATH", out string[] envPath))
                {
                    pathsToSearch.AddRange(envPath);
                }
                if (TryGetEnvironmentVariable("Path", out envPath))
                {
                    pathsToSearch.AddRange(envPath);
                }
            }

            for (int i = 0; i < pathsToSearch.Count; i++)
            {
                string envPath = pathsToSearch[i];
                if (!Directory.Exists(envPath))
                {
                    continue;
                }

                string[] results = Directory.GetFiles(envPath, $"{CLI_NAME}.*");
                if (results.Length > 0)
                {
                    return results[0];
                }
            }

            return null;

            static bool TryGetEnvironmentVariable(string envVar, out string[] strings)
            {
                string data = Environment.GetEnvironmentVariable(envVar);
                strings = data?.Split(';');

                return data != null;
            }
        }
    }

    private static class FallbackTextures
    {
        // We cache textures because many bindless textures have a performance overhead on AMD drivers
        // https://gist.github.com/BoyBaykiller/40d21d5b28391fb40d3f3bc348375ce8
        // We need to check if user deleted them and recreate if so because we do not own them

        private static BindlessSampledTexture cachedWhiteTexture = new BindlessSampledTexture();
        private static BindlessSampledTexture cachedPurpleBackTexture = new BindlessSampledTexture();

        public static BindlessSampledTexture GetWhite()
        {
            ref GLTexture texture = ref cachedWhiteTexture.SampledTexture.Texture;
            ref GLSampler sampler = ref cachedWhiteTexture.SampledTexture.Sampler;

            if (sampler == null || sampler.IsDeleted())
            {
                sampler = new GLSampler(new GLSampler.SamplerState());
            }

            if (texture == null || texture.IsDeleted())
            {
                texture = new GLTexture(GLTexture.Type.Texture2D);
                texture.Allocate(1, 1, 1, GLTexture.InternalFormat.R16G16B16A16Float);
                texture.Fill(new Vector4(1.0f));

                cachedWhiteTexture.BindlessHandle = texture.GetTextureHandleARB(sampler);
            }

            return cachedWhiteTexture;
        }

        public static BindlessSampledTexture GetPurpleBlack()
        {
            ref GLTexture texture = ref cachedPurpleBackTexture.SampledTexture.Texture;
            ref GLSampler sampler = ref cachedPurpleBackTexture.SampledTexture.Sampler;

            if (sampler == null || sampler.IsDeleted())
            {
                sampler = new GLSampler(new GLSampler.SamplerState()
                {
                    WrapModeS = GLSampler.WrapMode.Repeat,
                    WrapModeT = GLSampler.WrapMode.Repeat,
                });
            }

            if (texture == null || texture.IsDeleted())
            {
                texture = new GLTexture(GLTexture.Type.Texture2D);
                texture.Allocate(2, 2, 1, GLTexture.InternalFormat.R16G16B16A16Float);
                texture.Upload2D(2, 2, GLTexture.PixelFormat.RGB, GLTexture.PixelType.UByte, new byte[]
                {
                    // Source: https://en.wikipedia.org/wiki/File:Minecraft_missing_texture_block.svg
                    251,  62, 249, // Purple
                      0,   0,   0, // Black
                      0,   0,   0, // Black
                    251,  62, 249  // Purple
                }[0]);

                cachedPurpleBackTexture.BindlessHandle = texture.GetTextureHandleARB(sampler);
            }

            return cachedPurpleBackTexture;
        }
    }
}
