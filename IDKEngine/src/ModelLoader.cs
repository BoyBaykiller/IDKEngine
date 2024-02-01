using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using SharpGLTF.Schema2;
using SharpGLTF.Materials;
using Meshoptimizer;
using IDKEngine.Render.Objects;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;
using GLTexture = IDKEngine.Render.Objects.Texture;
using GltfTexture = SharpGLTF.Schema2.Texture;
using GltfTextureWrapMode = SharpGLTF.Schema2.TextureWrapMode;

namespace IDKEngine
{
    public static class ModelLoader
    {
        public static readonly string[] SupportedExtensions = new string[] { "KHR_materials_emissive_strength", "KHR_materials_volume", "KHR_materials_ior", "KHR_materials_transmission", "EXT_mesh_gpu_instancing" };

        public struct Model
        {
            public GpuDrawElementsCmd[] DrawCommands;
            public GpuMesh[] Meshes;
            public GpuMeshInstance[] MeshInstances;
            public GpuMaterial[] Materials;

            // Base geometry
            public GpuVertex[] Vertices;
            public Vector3[] VertexPositions;

            // Meshlet-rendering specific data
            public GpuMeshletTaskCmd[] MeshletTasksCmds;
            public GpuMeshlet[] Meshlets;
            public GpuMeshletInfo[] MeshletsInfo;
            public uint[] MeshletsVertexIndices;
            public byte[] MeshletsLocalIndices;

            // Vertex-rendering specific data
            public uint[] VertexIndices;
        }

        private struct TextureLoadData
        {
            public Image EncodedImage;
            public ColorComponents LoadComponents;
            public TextureMipMapFilter MinFilter;
            public TextureInterpolationFilter MagFilter;
            public GltfTextureWrapMode WrapS;
            public GltfTextureWrapMode WrapT;
            public SizedInternalFormat InternalFormat;
        }
        private struct MaterialLoadData
        {
            public static readonly int TEXTURE_COUNT = Enum.GetValues<TextureType>().Length;

            public enum TextureType : int
            {
                BaseColor,
                MetallicRoughness,
                Normal,
                Emissive,
                Transmission,
            }

            public ref TextureLoadData this[TextureType textureType]
            {
                get
                {
                    switch (textureType)
                    {
                        case TextureType.BaseColor: return ref Unsafe.AsRef(BaseColorTexture);
                        case TextureType.MetallicRoughness: return ref Unsafe.AsRef(MetallicRoughnessTexture);
                        case TextureType.Normal: return ref Unsafe.AsRef(NormalTexture);
                        case TextureType.Emissive: return ref Unsafe.AsRef(EmissiveTexture);
                        case TextureType.Transmission: return ref Unsafe.AsRef(TransmissionTexture);
                        default: throw new NotSupportedException($"Unsupported {nameof(TextureType)} {textureType}");
                    }
                }
            }

            public MaterialParams MaterialParams;

            public TextureLoadData BaseColorTexture;
            public TextureLoadData MetallicRoughnessTexture;
            public TextureLoadData NormalTexture;
            public TextureLoadData EmissiveTexture;
            public TextureLoadData TransmissionTexture;

            public static readonly MaterialLoadData Default = new MaterialLoadData()
            {
                BaseColorTexture = { },
                MetallicRoughnessTexture = { },
                NormalTexture = { },
                EmissiveTexture = { },
                TransmissionTexture = { },
                MaterialParams = MaterialParams.Default,
            };
        }
        private struct MaterialParams
        {
            public Vector3 EmissiveFactor;
            public Vector4 BaseColorFactor;
            public float TransmissionFactor;
            public float AlphaCutoff;
            public float RoughnessFactor;
            public float MetallicFactor;
            public Vector3 Absorbance;
            public float IOR;

            public static readonly MaterialParams Default = new MaterialParams()
            {
                EmissiveFactor = new Vector3(0.0f),
                BaseColorFactor = new Vector4(1.0f),
                TransmissionFactor = 0.0f,
                AlphaCutoff = 0.5f,
                RoughnessFactor = 1.0f,
                MetallicFactor = 1.0f,
                Absorbance = new Vector3(0.0f),
                IOR = 1.0f, // by spec 1.5 IOR would be correct
            };
        }
        private struct MeshletData
        {
            public Meshopt.Meshlet[] Meshlets;
            public int MeshletsLength;

            public uint[] VertexIndices;
            public int VertexIndicesLength;

            public byte[] LocalIndices;
            public int LocalIndicesLength;
        }

        public static Model GltfToEngineFormat(string path)
        {
            return GltfToEngineFormat(path, Matrix4.Identity);
        }

        public static Model GltfToEngineFormat(string path, Matrix4 rootTransform)
        {
            if (!File.Exists(path))
            {
                Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
                return new Model();
            }

            Stopwatch sw = Stopwatch.StartNew();

            Model model = LoadFromFile(path, rootTransform);

            Logger.Log(Logger.LogLevel.Info, $"Loaded {Path.GetFileName(path)} in {sw.ElapsedMilliseconds}ms (Triangles = {model.VertexIndices.Length / 3})");

            return model;
        }

        private static Model LoadFromFile(string path, Matrix4 rootTransform)
        {
            ModelRoot gltfFile = ModelRoot.Load(path, new ReadSettings() { Validation = SharpGLTF.Validation.ValidationMode.Skip });

            string gltfFilePathName = Path.GetFileName(path);
            foreach (string ext in gltfFile.ExtensionsUsed)
            {
                if (SupportedExtensions.Contains(ext))
                {
                    Logger.Log(Logger.LogLevel.Info, $"Model {gltfFilePathName} uses extension {ext}");
                }
                else
                {
                    Logger.Log(Logger.LogLevel.Warn, $"Model {gltfFilePathName} uses extension {ext} which is not supported");
                }
            }

            MaterialLoadData[] materialsLoadData = GetMaterialLoadDataFromGltf(gltfFile.LogicalMaterials);
            List<GpuMaterial> listMaterials = new List<GpuMaterial>(LoadGpuMaterials(materialsLoadData));

            List<GpuMesh> listMeshes = new List<GpuMesh>();
            List<GpuMeshInstance> listMeshInstances = new List<GpuMeshInstance>();
            List<GpuDrawElementsCmd> listDrawCommands = new List<GpuDrawElementsCmd>();
            List<GpuVertex> listVertices = new List<GpuVertex>();
            List<Vector3> listVertexPositions = new List<Vector3>();
            List<uint> listIndices = new List<uint>();
            List<GpuMeshletTaskCmd> listMeshetTasksCmd = new List<GpuMeshletTaskCmd>();
            List<GpuMeshlet> listMeshlets = new List<GpuMeshlet>();
            List<GpuMeshletInfo> listMeshletsInfo = new List<GpuMeshletInfo>();
            List<uint> listMeshletsVertexIndices = new List<uint>();
            List<byte> listMeshletsLocalIndices = new List<byte>();

            Stack<ValueTuple<Node, Matrix4>> nodeStack = new Stack<ValueTuple<Node, Matrix4>>();
            foreach (Node node in gltfFile.DefaultScene.VisualChildren)
            {
                nodeStack.Push((node, rootTransform));
            }

            while (nodeStack.Count > 0)
            {
                (Node node, Matrix4 parentNodeGlobalTransform) = nodeStack.Pop();

                Matrix4 nodeLocalTransform = node.LocalMatrix.ToOpenTK();
                Matrix4 nodeGlobalTransform = nodeLocalTransform * parentNodeGlobalTransform;
                
                foreach (Node child in node.VisualChildren)
                {
                    nodeStack.Push((child, nodeGlobalTransform));
                }

                Mesh gltfMesh = node.Mesh;
                if (gltfMesh == null)
                {
                    continue;
                }

                bool nodeHasGpuInstancing = TryLoadNodeInstances(node, out Matrix4[] nodeInstances);
                if (!nodeHasGpuInstancing)
                {
                    // If node doesn't use EXT_mesh_gpu_instancing to define instances we use standard local transform
                    nodeInstances = new Matrix4[1];
                    nodeInstances[0] = nodeLocalTransform;
                }

                //nodeInstances = new Matrix4[1000];
                //for (int i = 0; i < nodeInstances.Length; i++)
                //{
                //    Vector3 trans = Helper.RandomVec3(-15.0f, 15.0f);
                //    Vector3 rot = Helper.RandomVec3(0.0f, 2.0f * MathF.PI);
                //    var test = Matrix4.CreateRotationZ(rot.Z) *
                //               Matrix4.CreateRotationY(rot.Y) *
                //               Matrix4.CreateRotationX(rot.X) *
                //               Matrix4.CreateTranslation(trans);

                //    nodeInstances[i] = test;
                //}

                for (int i = 0; i < gltfMesh.Primitives.Count; i++)
                {
                    MeshPrimitive gltfMeshPrimitive = gltfMesh.Primitives[i];

                    (GpuVertex[] meshVertices, Vector3[] meshVertexPositions) = LoadGpuVertices(gltfMeshPrimitive);
                    uint[] meshIndices = LoadGpuIndices(gltfMeshPrimitive);

                    OptimizeMesh(ref meshVertices, ref meshVertexPositions, meshIndices);

                    MeshletData meshletData = GenerateMeshlets(meshVertexPositions, meshIndices);
                    (GpuMeshlet[] meshMeshlets, GpuMeshletInfo[] meshMeshletsInfo) = LoadGpuMeshlets(meshletData, meshVertexPositions);
                    for (int j = 0; j < meshMeshlets.Length; j++)
                    {
                        ref GpuMeshlet myMeshlet = ref meshMeshlets[j];

                        // Adjust offsets in context of all meshlets
                        myMeshlet.VertexOffset += (uint)listMeshletsVertexIndices.Count;
                        myMeshlet.IndicesOffset += (uint)listMeshletsLocalIndices.Count;
                    }

                    GpuMesh mesh = new GpuMesh();
                    mesh.InstanceCount = nodeInstances.Length;
                    mesh.EmissiveBias = 0.0f;
                    mesh.SpecularBias = 0.0f;
                    mesh.RoughnessBias = 0.0f;
                    mesh.TransmissionBias = 0.0f;
                    mesh.MeshletsStart = listMeshlets.Count;
                    mesh.MeshletCount = meshMeshlets.Length;
                    mesh.IORBias = 0.0f;
                    mesh.AbsorbanceBias = new Vector3(0.0f);
                    if (gltfMeshPrimitive.Material != null)
                    {
                        bool hasNormalMap = materialsLoadData[gltfMeshPrimitive.Material.LogicalIndex].NormalTexture.EncodedImage != null;
                        mesh.NormalMapStrength = hasNormalMap ? 1.0f : 0.0f;
                        mesh.MaterialIndex = gltfMeshPrimitive.Material.LogicalIndex;
                    }
                    else
                    {
                        GpuMaterial defaultGpuMaterial = LoadGpuMaterials(new MaterialLoadData[] { MaterialLoadData.Default })[0];
                        listMaterials.Add(defaultGpuMaterial);

                        mesh.NormalMapStrength = 0.0f;
                        mesh.MaterialIndex = listMaterials.Count - 1;
                    }

                    GpuMeshInstance[] meshInstances = new GpuMeshInstance[mesh.InstanceCount];
                    GpuMeshletTaskCmd[] meshletTaskCmds = new GpuMeshletTaskCmd[mesh.InstanceCount];
                    for (int j = 0; j < meshInstances.Length; j++)
                    {
                        ref GpuMeshInstance meshInstance = ref meshInstances[j];

                        meshInstance.ModelMatrix = nodeInstances[j] * parentNodeGlobalTransform;
                        meshInstance.MeshIndex = listMeshes.Count;


                        ref GpuMeshletTaskCmd meshletTaskCmd = ref meshletTaskCmds[j];
                        meshletTaskCmd.First = 0;
                        meshletTaskCmd.Count = (int)MathF.Ceiling(meshMeshlets.Length / 32.0f); // divide by task shader work group size
                    }

                    GpuDrawElementsCmd drawCmd = new GpuDrawElementsCmd();
                    drawCmd.IndexCount = meshIndices.Length;
                    drawCmd.InstanceCount = meshInstances.Length;
                    drawCmd.FirstIndex = listIndices.Count;
                    drawCmd.BaseVertex = listVertices.Count;
                    drawCmd.BaseInstance = listMeshInstances.Count;

                    listVertices.AddRange(meshVertices);
                    listVertexPositions.AddRange(meshVertexPositions);
                    listIndices.AddRange(meshIndices);
                    listMeshes.Add(mesh);
                    listMeshInstances.AddRange(meshInstances);
                    listDrawCommands.Add(drawCmd);
                    listMeshlets.AddRange(meshMeshlets);
                    listMeshletsInfo.AddRange(meshMeshletsInfo);
                    listMeshletsVertexIndices.AddRange(new ReadOnlySpan<uint>(meshletData.VertexIndices, 0, meshletData.VertexIndicesLength));
                    listMeshletsLocalIndices.AddRange(new ReadOnlySpan<byte>(meshletData.LocalIndices, 0, meshletData.LocalIndicesLength));
                    listMeshetTasksCmd.AddRange(meshletTaskCmds);
                }
            }

            Model model = new Model();
            model.Meshes = listMeshes.ToArray();
            model.MeshInstances = listMeshInstances.ToArray();
            model.Materials = listMaterials.ToArray();
            model.DrawCommands = listDrawCommands.ToArray();
            model.Vertices = listVertices.ToArray();
            model.VertexPositions = listVertexPositions.ToArray();
            model.VertexIndices = listIndices.ToArray();
            model.MeshletTasksCmds = listMeshetTasksCmd.ToArray();
            model.Meshlets = listMeshlets.ToArray();
            model.MeshletsInfo = listMeshletsInfo.ToArray();
            model.MeshletsVertexIndices = listMeshletsVertexIndices.ToArray();
            model.MeshletsLocalIndices = listMeshletsLocalIndices.ToArray();

            return model;
        }

        private static unsafe GpuMaterial[] LoadGpuMaterials(ReadOnlySpan<MaterialLoadData> materialsLoadData)
        {
            GLTexture defaultTexture = new GLTexture(TextureTarget2d.Texture2D);
            defaultTexture.ImmutableAllocate(1, 1, 1, SizedInternalFormat.Rgba16f);
            defaultTexture.Clear(PixelFormat.Rgba, PixelType.Float, new Vector4(1.0f));
            defaultTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            ulong defaultTextureHandle = defaultTexture.GetTextureHandleARB();

            GpuMaterial[] gpuMaterials = new GpuMaterial[materialsLoadData.Length];
            for (int i = 0; i < gpuMaterials.Length; i++)
            {
                ref readonly MaterialLoadData materialLoadData = ref materialsLoadData[i];
                ref GpuMaterial gpuMaterial = ref gpuMaterials[i];

                MaterialParams materialParams = materialLoadData.MaterialParams;
                gpuMaterial.EmissiveFactor = materialParams.EmissiveFactor;
                gpuMaterial.BaseColorFactor = Helper.CompressUR8G8B8A8(materialParams.BaseColorFactor);
                gpuMaterial.TransmissionFactor = materialParams.TransmissionFactor;
                gpuMaterial.AlphaCutoff = materialParams.AlphaCutoff;
                gpuMaterial.RoughnessFactor = materialParams.RoughnessFactor;
                gpuMaterial.MetallicFactor = materialParams.MetallicFactor;
                gpuMaterial.Absorbance = materialParams.Absorbance;
                gpuMaterial.IOR = materialParams.IOR;
                
                for (int j = 0; j < GpuMaterial.TEXTURE_COUNT; j++)
                {
                    GpuMaterial.TextureHandle textureType = (GpuMaterial.TextureHandle)j;
                    TextureLoadData textureLoadData = materialLoadData[(MaterialLoadData.TextureType)textureType];
                    if (textureLoadData.EncodedImage == null)
                    {
                        gpuMaterial[textureType] = defaultTextureHandle;
                        continue;
                    }

                    GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                    SamplerObject sampler = new SamplerObject();
                    if (textureType == GpuMaterial.TextureHandle.MetallicRoughness)
                    {
                        // By the spec "The metalness values are sampled from the B channel. The roughness values are sampled from the G channel"
                        // We "move" metallic from B into R channel, so it matches order of MetallicRoughness name
                        texture.SetSwizzleR(TextureSwizzle.Blue);
                    }

                    bool mipmapsRequired = IsMipMapFilter(textureLoadData.MinFilter);
                    int rawImageSizeInBytes = 0;
                    {
                        using Stream imageStream = textureLoadData.EncodedImage.Content.Open();

                        int imageWidth, imageHeight, comp;
                        StbImage.stbi__info_main(new StbImage.stbi__context(imageStream), &imageWidth, &imageHeight, &comp);

                        int levels = mipmapsRequired ? GLTexture.GetMaxMipmapLevel(imageWidth, imageHeight, 1) : 1;
                        texture.ImmutableAllocate(imageWidth, imageHeight, 1, textureLoadData.InternalFormat, levels);
                        if (mipmapsRequired)
                        {
                            sampler.SetSamplerParamter(SamplerParameterName.TextureMaxAnisotropyExt, 4.0f);
                        }
                        sampler.SetSamplerParamter(SamplerParameterName.TextureMinFilter, (int)textureLoadData.MinFilter);
                        sampler.SetSamplerParamter(SamplerParameterName.TextureMagFilter, (int)textureLoadData.MagFilter);
                        sampler.SetSamplerParamter(SamplerParameterName.TextureWrapS, (int)textureLoadData.WrapS);
                        sampler.SetSamplerParamter(SamplerParameterName.TextureWrapT, (int)textureLoadData.WrapT);

                        gpuMaterial[textureType] = texture.GetTextureHandleARB(sampler);

                        rawImageSizeInBytes = imageWidth * imageHeight * ColorComponentsToNumChannels(textureLoadData.LoadComponents);
                    }

                    TypedBuffer<byte> stagingBuffer = new TypedBuffer<byte>();
                    stagingBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.DeviceLocalHostVisible, rawImageSizeInBytes);

                    MainThreadQueue.Enqueue(() =>
                    {
                        // Leave some threads free for rendering
                        int threadPoolThreads = Math.Max(Environment.ProcessorCount - 2, 1);
                        ThreadPool.SetMaxThreads(threadPoolThreads, 1);
                        ThreadPool.SetMinThreads(threadPoolThreads, 1);

                        ThreadPool.QueueUserWorkItem((object state) =>
                        {
                            //Thread.Sleep(new Random(Thread.CurrentThread.ManagedThreadId).Next(600, 6000));

                            int imageWidth;
                            int imageHeight;
                            PixelFormat pixelFormat;
                            {
                                using Stream imageStream = textureLoadData.EncodedImage.Content.Open();

                                ImageResult imageResult = ImageResult.FromStream(imageStream, textureLoadData.LoadComponents);
                                Helper.MemCpy(imageResult.Data[0], ref Unsafe.AsRef<byte>(stagingBuffer.MappedMemory), imageResult.Data.Length);

                                imageWidth = imageResult.Width;
                                imageHeight = imageResult.Height;
                                pixelFormat = ColorComponentsToPixelFormat(imageResult.Comp);
                            }

                            MainThreadQueue.Enqueue(() =>
                            {
                                stagingBuffer.Bind(BufferTarget.PixelUnpackBuffer);
                                texture.SubTexture2D(imageWidth, imageHeight, pixelFormat, PixelType.UnsignedByte, IntPtr.Zero);
                                stagingBuffer.Dispose();

                                if (mipmapsRequired)
                                {
                                    texture.GenerateMipmap();
                                }
                            });
                        });
                    });
                }
            };

            return gpuMaterials;
        }

        private static MaterialLoadData[] GetMaterialLoadDataFromGltf(IReadOnlyList<Material> gltfMaterials)
        {
            MaterialLoadData[] materialsLoadData = new MaterialLoadData[gltfMaterials.Count];
            for (int i = 0; i < gltfMaterials.Count; i++)
            {
                Material gltfMaterial = gltfMaterials[i];
                MaterialLoadData materialLoadData = MaterialLoadData.Default;

                materialLoadData.MaterialParams = GetMaterialParams(gltfMaterial);

                for (int j = 0; j < MaterialLoadData.TEXTURE_COUNT; j++)
                {
                    MaterialLoadData.TextureType textureType = MaterialLoadData.TextureType.BaseColor + j;
                    materialLoadData[textureType] = GetTextureLoadData(gltfMaterial, textureType);
                }

                materialsLoadData[i] = materialLoadData;
                //materialsLoadData[i] = MaterialLoadData.Default;
            }

            return materialsLoadData;
        }
        private static TextureLoadData GetTextureLoadData(Material material, MaterialLoadData.TextureType textureType)
        {
            TextureLoadData textureLoadData = new TextureLoadData();
            MaterialChannel? materialChannel = null;

            if (textureType == MaterialLoadData.TextureType.BaseColor)
            {
                textureLoadData.InternalFormat = SizedInternalFormat.Srgb8Alpha8;
                //glTextureLoadData.InternalFormat = SizedInternalFormat.CompressedSrgbAlphaBptcUnorm;
                textureLoadData.LoadComponents = ColorComponents.RedGreenBlueAlpha;
                materialChannel = material.FindChannel(KnownChannel.BaseColor.ToString());
            }
            else if (textureType == MaterialLoadData.TextureType.MetallicRoughness)
            {
                textureLoadData.InternalFormat = SizedInternalFormat.R11fG11fB10f;
                textureLoadData.LoadComponents = ColorComponents.RedGreenBlue;
                materialChannel = material.FindChannel(KnownChannel.MetallicRoughness.ToString());
            }
            else if (textureType == MaterialLoadData.TextureType.Normal)
            {
                textureLoadData.InternalFormat = SizedInternalFormat.R11fG11fB10f;
                textureLoadData.LoadComponents = ColorComponents.RedGreenBlue;
                materialChannel = material.FindChannel(KnownChannel.Normal.ToString());
            }
            else if (textureType == MaterialLoadData.TextureType.Emissive)
            {
                // Can't pick compressed format because https://community.amd.com/t5/opengl-vulkan/opengl-bug-generating-mipmap-after-getting-texturehandle-causes/m-p/661233#M5107
                textureLoadData.InternalFormat = SizedInternalFormat.Srgb8Alpha8;
                //textureLoadData.InternalFormat = SizedInternalFormat.CompressedSrgbAlphaBptcUnorm;

                textureLoadData.LoadComponents = ColorComponents.RedGreenBlue;
                materialChannel = material.FindChannel(KnownChannel.Emissive.ToString());
            }
            else if (textureType == MaterialLoadData.TextureType.Transmission)
            {
                textureLoadData.InternalFormat = SizedInternalFormat.R8;
                textureLoadData.LoadComponents = ColorComponents.Grey;
                materialChannel = material.FindChannel(KnownChannel.Transmission.ToString());
            }

            GltfTexture gltfTexture = null;
            if (materialChannel.HasValue)
            {
                gltfTexture = materialChannel.Value.Texture;
            }

            if (gltfTexture == null)
            {
                return textureLoadData;
            }

            textureLoadData.EncodedImage = gltfTexture.PrimaryImage;

            if (gltfTexture.Sampler == null)
            {
                textureLoadData.WrapS = GltfTextureWrapMode.REPEAT;
                textureLoadData.WrapT = GltfTextureWrapMode.REPEAT;
                textureLoadData.MinFilter = TextureMipMapFilter.LINEAR_MIPMAP_LINEAR;
                textureLoadData.MagFilter = TextureInterpolationFilter.LINEAR;
            }
            else
            {
                textureLoadData.WrapT = gltfTexture.Sampler.WrapT;
                textureLoadData.WrapS = gltfTexture.Sampler.WrapS;
                textureLoadData.MinFilter = gltfTexture.Sampler.MinFilter;
                textureLoadData.MagFilter = gltfTexture.Sampler.MagFilter;
                if (gltfTexture.Sampler.MinFilter == TextureMipMapFilter.DEFAULT)
                {
                    textureLoadData.MinFilter = TextureMipMapFilter.LINEAR_MIPMAP_LINEAR;
                }
                if (gltfTexture.Sampler.MagFilter == TextureInterpolationFilter.DEFAULT)
                {
                    textureLoadData.MagFilter = TextureInterpolationFilter.LINEAR;
                }
            }

            return textureLoadData;
        }

        private static bool TryLoadNodeInstances(Node node, out Matrix4[] nodeInstances)
        {
            nodeInstances = null;

            MeshGpuInstancing gltfGpuInstancing = node.UseGpuInstancing();
            if (gltfGpuInstancing.Count == 0)
            {
                return false;
            }

            // gets instance defined as part of the EXT_mesh_gpu_instancing extension

            nodeInstances = new Matrix4[gltfGpuInstancing.Count];
            for (int i = 0; i < nodeInstances.Length; i++)
            {
                nodeInstances[i] = gltfGpuInstancing.GetLocalMatrix(i).ToOpenTK();
            }

            return true;
        }
        private static unsafe ValueTuple<GpuVertex[], Vector3[]> LoadGpuVertices(MeshPrimitive meshPrimitive)
        {
            const string GLTF_POSITION_ATTRIBUTE = "POSITION";
            const string GLTF_NORMAL_ATTRIBUTE = "NORMAL";
            const string GLTF_TEXCOORD_0_ATTRIBUTE = "TEXCOORD_0";

            Accessor positonAccessor = meshPrimitive.VertexAccessors[GLTF_POSITION_ATTRIBUTE];
            bool hasNormals = meshPrimitive.VertexAccessors.TryGetValue(GLTF_NORMAL_ATTRIBUTE, out Accessor normalAccessor);
            bool hasTexCoords = meshPrimitive.VertexAccessors.TryGetValue(GLTF_TEXCOORD_0_ATTRIBUTE, out Accessor texCoordAccessor);

            Vector3[] vertexPositions = new Vector3[positonAccessor.Count];
            GpuVertex[] vertices = new GpuVertex[positonAccessor.Count];

            {
                Span<Vector3> positions = MemoryMarshal.Cast<byte, Vector3>(positonAccessor.SourceBufferView.Content.AsSpan(positonAccessor.ByteOffset, positonAccessor.ByteLength));
                positions.CopyTo(vertexPositions);
            }

            if (hasNormals)
            {
                Span<Vector3> normals = MemoryMarshal.Cast<byte, Vector3>(normalAccessor.SourceBufferView.Content.AsSpan(normalAccessor.ByteOffset, normalAccessor.ByteLength));
                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 normal = normals[i];
                    vertices[i].Normal = Helper.CompressSR11G11B10(normal);

                    Vector3 c1 = Vector3.Cross(normal, Vector3.UnitZ);
                    Vector3 c2 = Vector3.Cross(normal, Vector3.UnitY);
                    Vector3 tangent = Vector3.Dot(c1, c1) > Vector3.Dot(c2, c2) ? c1 : c2;
                    vertices[i].Tangent = Helper.CompressSR11G11B10(tangent);
                }
            }

            if (hasTexCoords)
            {
                Span<Vector2> texCoords = MemoryMarshal.Cast<byte, Vector2>(texCoordAccessor.SourceBufferView.Content.AsSpan(texCoordAccessor.ByteOffset, texCoordAccessor.ByteLength));
                for (int i = 0; i < texCoords.Length; i++)
                {
                    vertices[i].TexCoord = texCoords[i];
                }
            }

            return (vertices, vertexPositions);
        }
        private static unsafe uint[] LoadGpuIndices(MeshPrimitive meshPrimitive)
        {
            Accessor accessor = meshPrimitive.IndexAccessor;
            uint[] vertexIndices = new uint[accessor.Count];
            int componentSize = ComponentTypeToSize(accessor.Encoding);

            Span<byte> rawData = accessor.SourceBufferView.Content.AsSpan(accessor.ByteOffset, accessor.ByteLength);
            for (int i = 0; i < vertexIndices.Length; i++)
            {
                Helper.MemCpy(rawData[componentSize * i], ref vertexIndices[i], componentSize);
            }

            return vertexIndices;
        }
        private static ValueTuple<GpuMeshlet[], GpuMeshletInfo[]> LoadGpuMeshlets(in MeshletData meshMeshletsData, ReadOnlySpan<Vector3> meshVertexPositions)
        {
            GpuMeshlet[] gpuMeshlets = new GpuMeshlet[meshMeshletsData.MeshletsLength];
            GpuMeshletInfo[] gpuMeshletsInfo = new GpuMeshletInfo[gpuMeshlets.Length];
            for (int i = 0; i < gpuMeshlets.Length; i++)
            {
                ref GpuMeshlet gpuMeshlet = ref gpuMeshlets[i];
                ref GpuMeshletInfo gpuMeshletInfo = ref gpuMeshletsInfo[i];
                ref readonly Meshopt.Meshlet meshOptMeshlet = ref meshMeshletsData.Meshlets[i];

                gpuMeshlet.VertexOffset = meshOptMeshlet.VertexOffset;
                gpuMeshlet.VertexCount = (byte)meshOptMeshlet.VertexCount;
                gpuMeshlet.IndicesOffset = meshOptMeshlet.TriangleOffset;
                gpuMeshlet.TriangleCount = (byte)meshOptMeshlet.TriangleCount;

                Box meshletBoundingBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                for (uint j = gpuMeshlet.VertexOffset; j < gpuMeshlet.VertexOffset + gpuMeshlet.VertexCount; j++)
                {
                    uint vertexIndex = meshMeshletsData.VertexIndices[j];
                    Vector3 pos = meshVertexPositions[(int)vertexIndex];
                    meshletBoundingBox.GrowToFit(pos);
                }
                gpuMeshletInfo.Min = meshletBoundingBox.Min;
                gpuMeshletInfo.Max = meshletBoundingBox.Max;
            }

            return (gpuMeshlets, gpuMeshletsInfo);
        }

        private static MaterialParams GetMaterialParams(Material gltfMaterial)
        {
            MaterialParams materialParams = MaterialParams.Default;
            materialParams.AlphaCutoff = gltfMaterial.AlphaCutoff;
            if (gltfMaterial.Alpha == SharpGLTF.Schema2.AlphaMode.OPAQUE)
            {
                materialParams.AlphaCutoff = -1.0f;
            }

            MaterialChannel? emissiveChannel = gltfMaterial.FindChannel(KnownChannel.Emissive.ToString());
            if (emissiveChannel.HasValue)
            {
                // KHR_materials_emissive_strength
                float emissiveStrength = 1.0f;
                AssignMaterialParamIfFound(ref emissiveStrength, emissiveChannel.Value, KnownProperty.EmissiveStrength);

                materialParams.EmissiveFactor = emissiveChannel.Value.Color.ToOpenTK().Xyz * emissiveStrength;
            }

            MaterialChannel? metallicRoughnessChannel = gltfMaterial.FindChannel(KnownChannel.MetallicRoughness.ToString());
            if (metallicRoughnessChannel.HasValue)
            {
                AssignMaterialParamIfFound(ref materialParams.RoughnessFactor, metallicRoughnessChannel.Value, KnownProperty.RoughnessFactor);
                AssignMaterialParamIfFound(ref materialParams.MetallicFactor, metallicRoughnessChannel.Value, KnownProperty.MetallicFactor);
            }

            MaterialChannel? baseColorChannel = gltfMaterial.FindChannel(KnownChannel.BaseColor.ToString());
            if (baseColorChannel.HasValue)
            {
                System.Numerics.Vector4 baseColor = new System.Numerics.Vector4();
                if (AssignMaterialParamIfFound(ref baseColor, baseColorChannel.Value, KnownProperty.RGBA))
                {
                    materialParams.BaseColorFactor = baseColor.ToOpenTK();
                }
            }

            MaterialChannel? transmissionChannel = gltfMaterial.FindChannel(KnownChannel.Transmission.ToString());
            if (transmissionChannel.HasValue) // KHR_materials_transmission
            {
                AssignMaterialParamIfFound(ref materialParams.TransmissionFactor, transmissionChannel.Value, KnownProperty.TransmissionFactor);

                if (materialParams.TransmissionFactor > 0.001f)
                {
                    // KHR_materials_ior
                    // This is here because I only want to set IOR for transmissive objects,
                    // because for opaque objects default value of 1.5 looks bad
                    materialParams.IOR = gltfMaterial.IndexOfRefraction;
                }
            }

            MaterialChannel? volumeAttenuationChannel = gltfMaterial.FindChannel(KnownChannel.VolumeAttenuation.ToString());
            if (volumeAttenuationChannel.HasValue) // KHR_materials_volume
            {
                Vector3 gltfAttenuationColor = new Vector3(1.0f);
                System.Numerics.Vector3 numericsGltfAttenuationColor = new System.Numerics.Vector3();
                if (AssignMaterialParamIfFound(ref numericsGltfAttenuationColor, volumeAttenuationChannel.Value, KnownProperty.RGB))
                {
                    gltfAttenuationColor = numericsGltfAttenuationColor.ToOpenTK();
                }

                float gltfAttenuationDistance = float.PositiveInfinity;
                AssignMaterialParamIfFound(ref gltfAttenuationDistance, volumeAttenuationChannel.Value, KnownProperty.AttenuationDistance);

                // Source: https://github.com/DassaultSystemes-Technology/dspbr-pt/blob/e7cfa6e9aab2b99065a90694e1f58564d675c1a4/packages/lib/shader/integrator/pt.glsl#L24
                // We can combine glTF Attenuation Color and Distance into a single Absorbance value
                float x = -MathF.Log(gltfAttenuationColor.X) / gltfAttenuationDistance;
                float y = -MathF.Log(gltfAttenuationColor.Y) / gltfAttenuationDistance;
                float z = -MathF.Log(gltfAttenuationColor.Z) / gltfAttenuationDistance;
                Vector3 absorbance = new Vector3(x, y, z);
                materialParams.Absorbance = absorbance;
            }

            return materialParams;
        }
        private static bool AssignMaterialParamIfFound<T>(ref T result, MaterialChannel materialChannel, KnownProperty property)
        {
            foreach (IMaterialParameter param in materialChannel.Parameters)
            {
                if (param.Name == property.ToString())
                {
                    result = (T)param.Value;
                    return true;
                }
            }

            return false;
        }

        private static int ComponentTypeToSize(EncodingType componentType)
        {
            int size = componentType switch
            {
                EncodingType.UNSIGNED_BYTE or EncodingType.BYTE => 1,
                EncodingType.UNSIGNED_SHORT or EncodingType.SHORT => 2,
                EncodingType.FLOAT or EncodingType.UNSIGNED_INT => 4,
                _ => throw new NotSupportedException($"Unsupported {nameof(EncodingType)} {componentType}"),
            };
            return size;
        }
        private static int ColorComponentsToNumChannels(ColorComponents colorComponents)
        {
            int numChannels = colorComponents switch
            {
                ColorComponents.Grey => 1,
                ColorComponents.GreyAlpha => 2,
                ColorComponents.RedGreenBlue => 3,
                ColorComponents.RedGreenBlueAlpha => 4,
                _ => throw new NotSupportedException($"Unsupported {nameof(ColorComponents)} {colorComponents}"),
            };
            return numChannels;
        }
        private static PixelFormat ColorComponentsToPixelFormat(ColorComponents colorComponents)
        {
            PixelFormat pixelFormat = colorComponents switch
            {
                ColorComponents.Grey => PixelFormat.Red,
                ColorComponents.GreyAlpha => PixelFormat.Rg,
                ColorComponents.RedGreenBlue => PixelFormat.Rgb,
                ColorComponents.RedGreenBlueAlpha => PixelFormat.Rgba,
                _ => throw new NotSupportedException($"Unsupported {nameof(ColorComponents)} {colorComponents}"),
            };
            return pixelFormat;
        }
        private static bool IsMipMapFilter(TextureMipMapFilter minFilterEnum)
        {
            return minFilterEnum == TextureMipMapFilter.NEAREST_MIPMAP_NEAREST ||
                   minFilterEnum == TextureMipMapFilter.LINEAR_MIPMAP_NEAREST ||
                   minFilterEnum == TextureMipMapFilter.NEAREST_MIPMAP_LINEAR ||
                   minFilterEnum == TextureMipMapFilter.LINEAR_MIPMAP_LINEAR;
        }

        private static unsafe void OptimizeMesh(ref GpuVertex[] meshVertices, ref Vector3[] meshVertexPositions, Span<uint> meshIndices)
        {
            const bool VERTEX_REMAP_OPTIMIZATION = false;
            const bool VERTEX_CACHE_OPTIMIZATION = true;
            const bool VERTEX_FETCH_OPTIMIZATION = false;

            uint[] remapTable = new uint[meshVertices.Length];
            if (VERTEX_REMAP_OPTIMIZATION)
            {
                nuint optimizedVertexCount = 0;
                fixed (void* meshVerticesPtr = meshVertices, meshPositionsPtr = meshVertexPositions)
                {
                    Span<Meshopt.Stream> vertexStreams = stackalloc Meshopt.Stream[2];
                    vertexStreams[0] = new Meshopt.Stream() { Data = meshVerticesPtr, Size = (nuint)sizeof(GpuVertex), Stride = (nuint)sizeof(GpuVertex) };
                    vertexStreams[1] = new Meshopt.Stream() { Data = meshPositionsPtr, Size = (nuint)sizeof(Vector3), Stride = (nuint)sizeof(Vector3) };

                    optimizedVertexCount = Meshopt.GenerateVertexRemapMulti(ref remapTable[0], meshIndices[0], (nuint)meshIndices.Length, (nuint)meshVertices.Length, vertexStreams[0], (nuint)vertexStreams.Length);

                    Meshopt.RemapIndexBuffer(ref meshIndices[0], meshIndices[0], (nuint)meshIndices.Length, remapTable[0]);
                    Meshopt.RemapVertexBuffer(vertexStreams[0].Data, vertexStreams[0].Data, (nuint)meshVertices.Length, vertexStreams[0].Stride, remapTable[0]);
                    Meshopt.RemapVertexBuffer(vertexStreams[1].Data, vertexStreams[1].Data, (nuint)meshVertices.Length, vertexStreams[1].Stride, remapTable[0]);
                }
                Array.Resize(ref meshVertices, (int)optimizedVertexCount);
                Array.Resize(ref meshVertexPositions, (int)optimizedVertexCount);
            }
            if (VERTEX_CACHE_OPTIMIZATION)
            {
                Meshopt.OptimizeVertexCache(ref meshIndices[0], meshIndices[0], (nuint)meshIndices.Length, (nuint)meshVertices.Length);
            }
            if (VERTEX_FETCH_OPTIMIZATION)
            {
                fixed (void* meshVerticesPtr = meshVertices, meshPositionsPtr = meshVertexPositions)
                {
                    Meshopt.OptimizeVertexFetchRemap(ref remapTable[0], meshIndices[0], (nuint)meshIndices.Length, (nuint)meshVertices.Length);

                    Meshopt.RemapIndexBuffer(ref meshIndices[0], meshIndices[0], (nuint)meshIndices.Length, remapTable[0]);
                    Meshopt.RemapVertexBuffer(meshVerticesPtr, meshVerticesPtr, (nuint)meshVertices.Length, (nuint)sizeof(GpuVertex), remapTable[0]);
                    Meshopt.RemapVertexBuffer(meshPositionsPtr, meshPositionsPtr, (nuint)meshVertexPositions.Length, (nuint)sizeof(Vector3), remapTable[0]);
                }
            }
        }
        private static unsafe MeshletData GenerateMeshlets(ReadOnlySpan<Vector3> meshVertexPositions, ReadOnlySpan<uint> meshIndices)
        {
            const float coneWeight = 0.0f;

            /// Keep in sync with mesh shader
            // perfectly fits 4 32-sized subgroups
            const uint MESHLET_MAX_VERTEX_COUNT = 128;

            // (252 * 3) + 4(hardware reserved) = 760bytes. Which almost perfectly fits NVIDIA-Turing 128 byte allocation granularity.
            // Note that meshoptimizer also requires this to be divisible by 4
            const uint MESHLET_MAX_TRIANGLE_COUNT = 252;

            nuint maxMeshlets = Meshopt.BuildMeshletsBound((nuint)meshIndices.Length, MESHLET_MAX_VERTEX_COUNT, MESHLET_MAX_TRIANGLE_COUNT);

            Meshopt.Meshlet[] meshlets = new Meshopt.Meshlet[maxMeshlets];
            uint[] meshletsVertexIndices = new uint[maxMeshlets * MESHLET_MAX_VERTEX_COUNT];
            byte[] meshletsPrimitiveIndices = new byte[maxMeshlets * MESHLET_MAX_TRIANGLE_COUNT * 3];
            nuint meshletCount = Meshopt.BuildMeshlets(ref meshlets[0],
                meshletsVertexIndices[0],
                meshletsPrimitiveIndices[0],
                meshIndices[0],
                (nuint)meshIndices.Length,
                meshVertexPositions[0].X,
                (nuint)meshVertexPositions.Length,
                (nuint)sizeof(Vector3),
                MESHLET_MAX_VERTEX_COUNT,
                MESHLET_MAX_TRIANGLE_COUNT,
                coneWeight);

            ref readonly Meshopt.Meshlet last = ref meshlets[meshletCount - 1];
            uint meshletsVertexIndicesLength = last.VertexOffset + last.VertexCount;
            uint meshletsLocalIndicesLength = last.TriangleOffset + ((last.TriangleCount * 3u + 3u) & ~3u);

            MeshletData result;
            result.Meshlets = meshlets;
            result.MeshletsLength = (int)meshletCount;

            result.VertexIndices = meshletsVertexIndices;
            result.VertexIndicesLength = (int)meshletsVertexIndicesLength;

            result.LocalIndices = meshletsPrimitiveIndices;
            result.LocalIndicesLength = (int)meshletsLocalIndicesLength;

            return result;
        }
    }
}
