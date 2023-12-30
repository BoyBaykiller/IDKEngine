using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using glTFLoader;
using glTFLoader.Schema;
using Meshoptimizer;
using IDKEngine.Shapes;
using IDKEngine.Render.Objects;
using GLTexture = IDKEngine.Render.Objects.Texture;
using GltfTexture = glTFLoader.Schema.Texture;

namespace IDKEngine
{
    class Model
    {
        private struct GpuTextureLoadData
        {
            public ImageResult Image;
            public Sampler Sampler;
            public SizedInternalFormat InternalFormat;
        }
        public struct MaterialDetails
        {
            public Vector3 EmissiveFactor;
            public uint BaseColorFactor;
            public float MetallicFactor;
            public float RoughnessFactor;
            public float AlphaCutoff;

            public static readonly MaterialDetails Default = new MaterialDetails()
            {
                EmissiveFactor = new Vector3(0.0f),
                BaseColorFactor = Helper.CompressUR8G8B8A8(new Vector4(1.0f)),
                MetallicFactor = 1.0f,
                RoughnessFactor = 1.0f,
                AlphaCutoff = 0.5f,
            };
        }
        private struct GpuMaterialLoadData
        {
            public const int TEXTURE_COUNT = 4;
            
            public enum TextureType : int
            {
                BaseColor,
                MetallicRoughness,
                Normal,
                Emissive,
            }

            public ref GpuTextureLoadData this[TextureType textureType]
            {
                get
                {
                    switch (textureType)
                    {
                        case TextureType.BaseColor: return ref Unsafe.AsRef(BaseColorTexture);
                        case TextureType.MetallicRoughness: return ref Unsafe.AsRef(MetallicRoughnessTexture);
                        case TextureType.Normal: return ref Unsafe.AsRef(NormalTexture);
                        case TextureType.Emissive: return ref Unsafe.AsRef(EmissiveTexture);
                        default: throw new NotSupportedException($"Unsupported {nameof(TextureType)} {textureType}");
                    }
                }
            }

            public GpuTextureLoadData BaseColorTexture;
            public GpuTextureLoadData MetallicRoughnessTexture;
            public GpuTextureLoadData NormalTexture;
            public GpuTextureLoadData EmissiveTexture;
            public MaterialDetails MaterialDetails;

            public static readonly GpuMaterialLoadData Default = new GpuMaterialLoadData()
            {
                BaseColorTexture = { },
                MetallicRoughnessTexture = { },
                NormalTexture = { },
                EmissiveTexture = { },
                MaterialDetails = MaterialDetails.Default,
            };
        }
        private struct MeshMeshletsData
        {
            public Meshopt.Meshlet[] Meshlets;
            public int MeshletsLength;

            public uint[] VertexIndices;
            public int VertexIndicesLength;

            public byte[] LocalIndices;
            public int LocalIndicesLength;
        }


        public GpuMesh[] Meshes;
        public GpuMeshInstance[] MeshInstances;
        public GpuMaterial[] Materials;
        public GpuDrawElementsCmd[] DrawCommands;
        public GpuVertex[] Vertices;
        public Vector3[] VertexPositions;
        public uint[] Indices;

        public GpuMeshletTaskCmd[] MeshTasksCmds;
        public GpuMeshlet[] Meshlets;
        public GpuMeshletInfo[] MeshletsInfo;
        public uint[] MeshletsVertexIndices;
        public byte[] MeshletsLocalIndices;

        private Gltf gltfModel;
        private string RootDir;
        public Model(string path)
            : this(path, Matrix4.Identity)
        {
        }

        public Model(string path, Matrix4 rootTransform)
        {
            if (!File.Exists(path))
            {
                Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
                return;
            }

            LoadFromFile(path, rootTransform);
            Logger.Log(Logger.LogLevel.Info, $"Loaded model {path}");
        }

        public unsafe void LoadFromFile(string path, Matrix4 rootTransform)
        {
            gltfModel = Interface.LoadModel(path);

            RootDir = Path.GetDirectoryName(path);

            GpuMaterialLoadData[] gpuMaterialsLoadData = GetGpuMaterialLoadDataFromGltf(gltfModel.Materials, gltfModel.Textures);
            List<GpuMaterial> listMaterials = new List<GpuMaterial>(LoadGpuMaterials(gpuMaterialsLoadData));
            List<GpuMesh> listMeshes = new List<GpuMesh>();
            List<GpuMeshInstance> listMeshInstances = new List<GpuMeshInstance>();
            List<GpuDrawElementsCmd> listDrawCommands = new List<GpuDrawElementsCmd>();
            List<GpuVertex> listVertices = new List<GpuVertex>();
            List<Vector3> listVertexPositions = new List<Vector3>();
            List<uint> listIndices = new List<uint>();
            List<GpuMeshletTaskCmd> listMeshTasksCmd = new List<GpuMeshletTaskCmd>();
            List<GpuMeshlet> listMeshlets = new List<GpuMeshlet>();
            List<GpuMeshletInfo> listMeshletsInfo = new List<GpuMeshletInfo>();
            List<uint> listMeshletsVertexIndices = new List<uint>();
            List<byte> listMeshletsLocalIndices = new List<byte>();

            Stack<ValueTuple<Node, Matrix4>> nodeStack = new Stack<ValueTuple<Node, Matrix4>>();

            // Push all root nodes (of first scene only)
            for (int i = 0; i < gltfModel.Scenes[0].Nodes.Length; i++)
            {
                Node rootNode = gltfModel.Nodes[gltfModel.Scenes[0].Nodes[i]];
                nodeStack.Push((rootNode, rootTransform));
            }

            while (nodeStack.Count > 0)
            {
                (Node node, Matrix4 globalParentTransform) = nodeStack.Pop();
                Matrix4 localModelMatrix = GetNodeModelMatrix(node);
                Matrix4 globalModelMatrix = localModelMatrix * globalParentTransform;

                if (node.Children != null)
                {
                    for (int i = 0; i < node.Children.Length; i++)
                    {
                        Node childNode = gltfModel.Nodes[node.Children[i]];
                        nodeStack.Push((childNode, globalModelMatrix));
                    }
                }

                if (node.Mesh.HasValue)
                {
                    Mesh gltfMesh = gltfModel.Meshes[node.Mesh.Value];
                    for (int i = 0; i < gltfMesh.Primitives.Length; i++)
                    {
                        MeshPrimitive gltfMeshPrimitive = gltfMesh.Primitives[i];

                        (GpuVertex[] meshVertices, Vector3[] meshVertexPositions) = LoadGpuVertexData(gltfMeshPrimitive);
                        uint[] meshIndices = LoadGpuIndexData(gltfMeshPrimitive);

                        RunMeshOptimizations(ref meshVertices, ref meshVertexPositions, meshIndices);
                        MeshMeshletsData meshMeshletsData = GenerateMeshMeshletData(meshVertexPositions, meshIndices);

                        GpuMeshlet[] meshMeshlets = new GpuMeshlet[meshMeshletsData.MeshletsLength];
                        GpuMeshletInfo[] meshMeshletsInfo = new GpuMeshletInfo[meshMeshlets.Length];
                        for (int j = 0; j < meshMeshlets.Length; j++)
                        {
                            ref GpuMeshlet myMeshlet = ref meshMeshlets[j];
                            ref readonly Meshopt.Meshlet meshOptMeshlet = ref meshMeshletsData.Meshlets[j];

                            myMeshlet.VertexOffset = meshOptMeshlet.VertexOffset;
                            myMeshlet.VertexCount = (byte)meshOptMeshlet.VertexCount;
                            myMeshlet.IndicesOffset = meshOptMeshlet.TriangleOffset;
                            myMeshlet.TriangleCount = (byte)meshOptMeshlet.TriangleCount;

                            Box meshletBoundingBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                            for (int k = 0; k < myMeshlet.VertexCount; k++)
                            {
                                uint vertexIndex = meshMeshletsData.VertexIndices[myMeshlet.VertexOffset + k];
                                Vector3 pos = meshVertexPositions[vertexIndex];
                                meshletBoundingBox.GrowToFit(pos);
                            }
                            meshMeshletsInfo[j].Min = meshletBoundingBox.Min;
                            meshMeshletsInfo[j].Max = meshletBoundingBox.Max;

                            // Adjust offsets in context of all meshes
                            myMeshlet.VertexOffset += (uint)listMeshletsVertexIndices.Count;
                            myMeshlet.IndicesOffset += (uint)listMeshletsLocalIndices.Count;
                        }

                        GpuMesh mesh = new GpuMesh();
                        mesh.EmissiveBias = 0.0f;
                        mesh.SpecularBias = 0.0f;
                        mesh.RoughnessBias = 0.0f;
                        mesh.RefractionChance = 0.0f;
                        mesh.MeshletsStart = listMeshlets.Count;
                        mesh.MeshletCount = meshMeshlets.Length;
                        mesh.IOR = 1.0f;
                        mesh.Absorbance = new Vector3(0.0f);
                        if (gltfMeshPrimitive.Material.HasValue)
                        {
                            bool hasNormalMap = gpuMaterialsLoadData[gltfMeshPrimitive.Material.Value].NormalTexture.Image != null;
                            mesh.NormalMapStrength = hasNormalMap ? 1.0f : 0.0f;
                            mesh.MaterialIndex = gltfMeshPrimitive.Material.Value;
                        }
                        else
                        {
                            GpuMaterial defaultGpuMaterial = LoadGpuMaterials(new GpuMaterialLoadData[] { GpuMaterialLoadData.Default })[0];
                            listMaterials.Add(defaultGpuMaterial);

                            mesh.NormalMapStrength = 0.0f;
                            mesh.MaterialIndex = listMaterials.Count - 1;
                        }

                        GpuMeshInstance meshInstance = new GpuMeshInstance();
                        meshInstance.ModelMatrix = globalModelMatrix;

                        GpuDrawElementsCmd drawCmd = new GpuDrawElementsCmd();
                        drawCmd.IndexCount = meshIndices.Length;
                        drawCmd.InstanceCount = 1;
                        drawCmd.FirstIndex = listIndices.Count;
                        drawCmd.BaseVertex = listVertices.Count;
                        drawCmd.BaseInstance = listDrawCommands.Count;

                        GpuMeshletTaskCmd meshTaskCmd = new GpuMeshletTaskCmd();
                        meshTaskCmd.First = 0; // listMeshlets.Count
                        meshTaskCmd.Count = (int)MathF.Ceiling(meshMeshletsData.Meshlets.Length / 32.0f); // divide by task shader work group size

                        listVertices.AddRange(meshVertices);
                        listVertexPositions.AddRange(meshVertexPositions);
                        listIndices.AddRange(meshIndices);
                        listMeshes.Add(mesh);
                        listMeshInstances.Add(meshInstance);
                        listDrawCommands.Add(drawCmd);
                        listMeshlets.AddRange(meshMeshlets);
                        listMeshletsInfo.AddRange(meshMeshletsInfo);
                        listMeshletsVertexIndices.AddRange(new ReadOnlySpan<uint>(meshMeshletsData.VertexIndices, 0, meshMeshletsData.VertexIndicesLength));
                        listMeshletsLocalIndices.AddRange(new ReadOnlySpan<byte>(meshMeshletsData.LocalIndices, 0, meshMeshletsData.LocalIndicesLength));
                        listMeshTasksCmd.Add(meshTaskCmd);
                    }
                }
            }

            Meshes = listMeshes.ToArray();
            MeshInstances = listMeshInstances.ToArray();
            Materials = listMaterials.ToArray();
            DrawCommands = listDrawCommands.ToArray();
            Vertices = listVertices.ToArray();
            VertexPositions = listVertexPositions.ToArray();
            Indices = listIndices.ToArray();
            MeshTasksCmds = listMeshTasksCmd.ToArray();
            Meshlets = listMeshlets.ToArray();
            MeshletsInfo = listMeshletsInfo.ToArray();
            MeshletsVertexIndices = listMeshletsVertexIndices.ToArray();
            MeshletsLocalIndices = listMeshletsLocalIndices.ToArray();
        }

        private unsafe GpuMaterialLoadData[] GetGpuMaterialLoadDataFromGltf(Material[] materials, GltfTexture[] textures)
        {
            if (materials == null)
            {
                return Array.Empty<GpuMaterialLoadData>();
            }

            GpuMaterialLoadData[] materialsLoadData = new GpuMaterialLoadData[materials.Length];

            //for (int i = 0; i < materialsLoadData.Length; i++)
            //{
            //    materialsLoadData[i] = GpuMaterialLoadData.Default;
            //}
            //return materialsLoadData;

            Parallel.For(0, materialsLoadData.Length * GpuMaterialLoadData.TEXTURE_COUNT, i =>
            {
                int materialIndex = i / GpuMaterialLoadData.TEXTURE_COUNT;
                GpuMaterialLoadData.TextureType textureType = (GpuMaterialLoadData.TextureType)(i % GpuMaterialLoadData.TEXTURE_COUNT);

                Material material = materials[materialIndex];
                ref GpuMaterialLoadData materialData = ref materialsLoadData[materialIndex];
                
                // Let one thread load non image data
                if (textureType == GpuMaterialLoadData.TextureType.BaseColor)
                {
                    materialData.MaterialDetails = MaterialDetails.Default;
                    materialData.MaterialDetails.AlphaCutoff = material.AlphaCutoff;

                    if (material.EmissiveFactor != null)
                    {
                        materialData.MaterialDetails.EmissiveFactor = new Vector3(
                            material.EmissiveFactor[0],
                            material.EmissiveFactor[1],
                            material.EmissiveFactor[2]);
                    }
                    if (material.PbrMetallicRoughness != null)
                    {
                        if (material.PbrMetallicRoughness.BaseColorFactor != null)
                        {
                            materialData.MaterialDetails.BaseColorFactor = Helper.CompressUR8G8B8A8(new Vector4(
                                material.PbrMetallicRoughness.BaseColorFactor[0],
                                material.PbrMetallicRoughness.BaseColorFactor[1],
                                material.PbrMetallicRoughness.BaseColorFactor[2],
                                material.PbrMetallicRoughness.BaseColorFactor[3]));
                        }

                        materialData.MaterialDetails.RoughnessFactor = material.PbrMetallicRoughness.RoughnessFactor;
                        materialData.MaterialDetails.MetallicFactor = material.PbrMetallicRoughness.MetallicFactor;
                    }
                }

                materialData[textureType] = GetGLTextureLoadData(material, textures, textureType);
            });

            GpuTextureLoadData GetGLTextureLoadData(Material material, GltfTexture[] textures, GpuMaterialLoadData.TextureType textureType)
            {
                GpuTextureLoadData glTextureLoadData = new GpuTextureLoadData();
                GltfTexture imageToLoad = null;
                ColorComponents imageColorComponents = ColorComponents.RedGreenBlueAlpha;
                {
                    if (textureType == GpuMaterialLoadData.TextureType.BaseColor)
                    {
                        glTextureLoadData.InternalFormat = SizedInternalFormat.Srgb8Alpha8;
                        //glTextureLoadData.InternalFormat = SizedInternalFormat.CompressedSrgbAlphaBptcUnorm;
                        imageColorComponents = ColorComponents.RedGreenBlueAlpha;

                        TextureInfo textureInfo = null;
                        MaterialPbrMetallicRoughness materialPbrMetallicRoughness = material.PbrMetallicRoughness;
                        if (materialPbrMetallicRoughness != null) textureInfo = materialPbrMetallicRoughness.BaseColorTexture;
                        if (textureInfo != null) imageToLoad = textures[textureInfo.Index];
                    }
                    else if (textureType == GpuMaterialLoadData.TextureType.MetallicRoughness)
                    {
                        glTextureLoadData.InternalFormat = SizedInternalFormat.R11fG11fB10f;
                        imageColorComponents = ColorComponents.RedGreenBlue;

                        TextureInfo textureInfo = null;
                        MaterialPbrMetallicRoughness materialPbrMetallicRoughness = material.PbrMetallicRoughness;
                        if (materialPbrMetallicRoughness != null) textureInfo = materialPbrMetallicRoughness.MetallicRoughnessTexture;
                        if (textureInfo != null) imageToLoad = textures[textureInfo.Index];
                    }
                    else if (textureType == GpuMaterialLoadData.TextureType.Normal)
                    {
                        glTextureLoadData.InternalFormat = SizedInternalFormat.R11fG11fB10f;
                        imageColorComponents = ColorComponents.RedGreenBlue;

                        MaterialNormalTextureInfo textureInfo = material.NormalTexture;
                        if (textureInfo != null) imageToLoad = textures[textureInfo.Index];
                    }
                    else if (textureType == GpuMaterialLoadData.TextureType.Emissive)
                    {
                        glTextureLoadData.InternalFormat = SizedInternalFormat.CompressedSrgbAlphaBptcUnorm;
                        imageColorComponents = ColorComponents.RedGreenBlue;

                        TextureInfo textureInfo = material.EmissiveTexture;
                        if (textureInfo != null) imageToLoad = textures[textureInfo.Index];
                    }
                }

                {
                    bool shouldReportMissingTexture = textureType == GpuMaterialLoadData.TextureType.BaseColor ||
                                                      textureType == GpuMaterialLoadData.TextureType.MetallicRoughness ||
                                                      textureType == GpuMaterialLoadData.TextureType.Normal;
                    if (shouldReportMissingTexture && (imageToLoad == null || !imageToLoad.Source.HasValue))
                    {
                        Logger.Log(Logger.LogLevel.Warn, $"Material {material.Name} has no texture of type {textureType}");
                    }
                }

                if (imageToLoad == null)
                {
                    return glTextureLoadData; 
                }

                if (imageToLoad.Source.HasValue)
                {
                    Image image = gltfModel.Images[imageToLoad.Source.Value];
                    string imagePath = Path.Combine(RootDir, image.Uri);

                    if (!File.Exists(imagePath))
                    {
                        Logger.Log(Logger.LogLevel.Error, $"Image \"{imagePath}\" is not found");
                        return glTextureLoadData;
                    }

                    using FileStream stream = File.OpenRead(imagePath);
                    glTextureLoadData.Image = ImageResult.FromStream(stream, imageColorComponents);
                }
                
                if (imageToLoad.Sampler.HasValue)
                {
                    glTextureLoadData.Sampler = gltfModel.Samplers[imageToLoad.Sampler.Value];
                }

                return glTextureLoadData;
            }

            return materialsLoadData;
        }

        private unsafe ValueTuple<GpuVertex[], Vector3[]> LoadGpuVertexData(MeshPrimitive meshPrimitive)
        {
            const string GLTF_POSITION_ATTRIBUTE = "POSITION";
            const string GLTF_NORMAL_ATTRIBUTE = "NORMAL";
            const string GLTF_TEXCOORD_0_ATTRIBUTE = "TEXCOORD_0";

            GpuVertex[] vertices = Array.Empty<GpuVertex>();
            Vector3[] vertexPositions = Array.Empty<Vector3>();

            Dictionary<string, int> myDict = meshPrimitive.Attributes;
            for (int j = 0; j < myDict.Count; j++)
            {
                KeyValuePair<string, int> item = myDict.ElementAt(j);

                Accessor accessor = gltfModel.Accessors[item.Value];
                BufferView bufferView = gltfModel.BufferViews[accessor.BufferView.Value];
                glTFLoader.Schema.Buffer buffer = gltfModel.Buffers[bufferView.Buffer];

                using FileStream fileStream = File.OpenRead(Path.Combine(RootDir, buffer.Uri));
                fileStream.Position = accessor.ByteOffset + bufferView.ByteOffset;

                if (vertexPositions == Array.Empty<Vector3>())
                {
                    vertices = new GpuVertex[accessor.Count];
                    vertexPositions = new Vector3[accessor.Count];
                }

                if (item.Key == GLTF_POSITION_ATTRIBUTE)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 data;
                        Span<byte> span = new Span<byte>(&data, sizeof(Vector3));
                        fileStream.Read(span);

                        vertexPositions[i] = data;
                    }
                }
                else if (item.Key == GLTF_NORMAL_ATTRIBUTE)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 data;
                        Span<byte> span = new Span<byte>(&data, sizeof(Vector3));
                        fileStream.Read(span);

                        Vector3 normal = data;
                        vertices[i].Normal = Helper.CompressSR11G11B10(normal);

                        Vector3 c1 = Vector3.Cross(normal, Vector3.UnitZ);
                        Vector3 c2 = Vector3.Cross(normal, Vector3.UnitY);
                        Vector3 tangent = Vector3.Dot(c1, c1) > Vector3.Dot(c2, c2) ? c1 : c2;
                        vertices[i].Tangent = Helper.CompressSR11G11B10(tangent);
                    }
                }
                else if (item.Key == GLTF_TEXCOORD_0_ATTRIBUTE)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector2 data;
                        Span<byte> span = new Span<byte>(&data, sizeof(Vector2));
                        fileStream.Read(span);

                        vertices[i].TexCoord = data;
                    }
                }
            }

            return (vertices, vertexPositions);
        }
        private unsafe uint[] LoadGpuIndexData(MeshPrimitive meshPrimitive)
        {
            Accessor accessor = gltfModel.Accessors[meshPrimitive.Indices.Value];
            BufferView bufferView = gltfModel.BufferViews[accessor.BufferView.Value];
            glTFLoader.Schema.Buffer buffer = gltfModel.Buffers[bufferView.Buffer];

            using FileStream fileStream = File.OpenRead(Path.Combine(RootDir, buffer.Uri));
            fileStream.Position = accessor.ByteOffset + bufferView.ByteOffset;

            uint[] indices = new uint[accessor.Count];
            int componentSize = ComponentTypeToSize(accessor.ComponentType);
            for (int j = 0; j < accessor.Count; j++)
            {
                uint data;
                Span<byte> span = new Span<byte>(&data, componentSize);
                fileStream.Read(span);

                indices[j] = data;
            }

            return indices;
        }

        private static GpuMaterial[] LoadGpuMaterials(ReadOnlySpan<GpuMaterialLoadData> materialsLoadInfo)
        {
            GLTexture defaultTexture = new GLTexture(TextureTarget2d.Texture2D);
            defaultTexture.ImmutableAllocate(1, 1, 1, SizedInternalFormat.Rgba16f);
            defaultTexture.Clear(PixelFormat.Rgba, PixelType.Float, new Vector4(1.0f));
            defaultTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            ulong defaultTextureHandle = defaultTexture.GetTextureHandleARB();

            GpuMaterial[] materials = new GpuMaterial[materialsLoadInfo.Length];    
            for (int i = 0; i < materials.Length; i++)
            {
                ref GpuMaterial gpuMaterial = ref materials[i];
                ref readonly GpuMaterialLoadData materialLoadData = ref materialsLoadInfo[i];

                gpuMaterial.EmissiveFactor = materialLoadData.MaterialDetails.EmissiveFactor;
                gpuMaterial.BaseColorFactor = materialLoadData.MaterialDetails.BaseColorFactor;
                gpuMaterial.RoughnessFactor = materialLoadData.MaterialDetails.RoughnessFactor;
                gpuMaterial.MetallicFactor = materialLoadData.MaterialDetails.MetallicFactor;
                gpuMaterial.AlphaCutoff = materialLoadData.MaterialDetails.AlphaCutoff;

                {
                    if (materialLoadData.BaseColorTexture.Image == null)
                    {
                        gpuMaterial.BaseColorTextureHandle = defaultTextureHandle;
                    }
                    else
                    {
                        GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                        SamplerObject sampler = new SamplerObject();
                        ConfigureGLTextureAndSampler(materialLoadData.BaseColorTexture, texture, sampler);
                        gpuMaterial.BaseColorTextureHandle = texture.GetTextureHandleARB(sampler);
                    }
                }
                {
                    if (materialLoadData.MetallicRoughnessTexture.Image == null)
                    {
                        gpuMaterial.MetallicRoughnessTextureHandle = defaultTextureHandle;
                    }
                    else
                    {
                        GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                        SamplerObject sampler = new SamplerObject();
                        ConfigureGLTextureAndSampler(materialLoadData.MetallicRoughnessTexture, texture, sampler);
                        texture.SetSwizzleR(All.Blue); // "Move" metallic from Blue into Red channel
                        gpuMaterial.MetallicRoughnessTextureHandle = texture.GetTextureHandleARB(sampler);
                    }
                }
                {
                    if (materialLoadData.NormalTexture.Image == null)
                    {
                        gpuMaterial.NormalTextureHandle = defaultTextureHandle;
                    }
                    else
                    {
                        GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                        SamplerObject sampler = new SamplerObject();
                        ConfigureGLTextureAndSampler(materialLoadData.NormalTexture, texture, sampler);
                        gpuMaterial.NormalTextureHandle = texture.GetTextureHandleARB(sampler);
                    }
                }
                {
                    if (materialLoadData.EmissiveTexture.Image == null)
                    {
                        gpuMaterial.EmissiveTextureHandle = defaultTextureHandle;
                    }
                    else
                    {
                        GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                        SamplerObject sampler = new SamplerObject();
                        ConfigureGLTextureAndSampler(materialLoadData.EmissiveTexture, texture, sampler);
                        gpuMaterial.EmissiveTextureHandle = texture.GetTextureHandleARB(sampler);
                    }
                }
            }

            return materials;
        }
        private static void ConfigureGLTextureAndSampler(GpuTextureLoadData data, GLTexture texture, SamplerObject sampler)
        {
            if (data.Sampler == null)
            {
                data.Sampler = new Sampler();
                data.Sampler.WrapT = Sampler.WrapTEnum.REPEAT;
                data.Sampler.WrapS = Sampler.WrapSEnum.REPEAT;
                data.Sampler.MinFilter = null;
                data.Sampler.MagFilter = null;
            }

            data.Sampler.MinFilter ??= Sampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR;
            data.Sampler.MagFilter ??= Sampler.MagFilterEnum.LINEAR;

            bool mipmapsRequired = IsMipMapFilter(data.Sampler.MinFilter.Value);
            int levels = mipmapsRequired ? Math.Max(GLTexture.GetMaxMipmapLevel(data.Image.Width, data.Image.Height, 1), 1) : 1;
            texture.ImmutableAllocate(data.Image.Width, data.Image.Height, 1, data.InternalFormat, levels);
            texture.SubTexture2D(data.Image.Width, data.Image.Height, ColorComponentsToPixelFormat(data.Image.Comp), PixelType.UnsignedByte, data.Image.Data);

            if (mipmapsRequired)
            {
                sampler.SetSamplerParamter(SamplerParameterName.TextureMaxAnisotropyExt, 4.0f);
                texture.GenerateMipmap();
            }
            sampler.SetSamplerParamter(SamplerParameterName.TextureMinFilter, (int)data.Sampler.MinFilter);
            sampler.SetSamplerParamter(SamplerParameterName.TextureMagFilter, (int)data.Sampler.MagFilter);
            sampler.SetSamplerParamter(SamplerParameterName.TextureWrapS, (int)data.Sampler.WrapS);
            sampler.SetSamplerParamter(SamplerParameterName.TextureWrapT, (int)data.Sampler.WrapT);
        }

        private static Matrix4 GetNodeModelMatrix(Node node)
        {
            Matrix4 modelMatrix = new Matrix4(
                    node.Matrix[0], node.Matrix[1], node.Matrix[2], node.Matrix[3],
                    node.Matrix[4], node.Matrix[5], node.Matrix[6], node.Matrix[7],
                    node.Matrix[8], node.Matrix[9], node.Matrix[10], node.Matrix[11],
                    node.Matrix[12], node.Matrix[13], node.Matrix[14], node.Matrix[15]);

            if (modelMatrix == Matrix4.Identity)
            {
                Quaternion rotation = new Quaternion(1.0f, 0.0f, 0.0f, 0.0f);
                Vector3 scale = new Vector3(1.0f);
                Vector3 translation = new Vector3(0.0f);

                if (node.Rotation.Length == 4)
                {
                    rotation = new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3]);
                }

                if (node.Scale.Length == 3)
                {
                    scale = new Vector3(node.Scale[0], node.Scale[1], node.Scale[2]);
                }

                if (node.Translation.Length == 3)
                {
                    translation = new Vector3(node.Translation[0], node.Translation[1], node.Translation[2]);
                }

                modelMatrix = Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(rotation) * Matrix4.CreateTranslation(translation);
            }

            return modelMatrix;
        }

        private static int ComponentTypeToSize(Accessor.ComponentTypeEnum componentType)
        {
            int size = componentType switch
            {
                Accessor.ComponentTypeEnum.UNSIGNED_BYTE or Accessor.ComponentTypeEnum.BYTE => 1,
                Accessor.ComponentTypeEnum.UNSIGNED_SHORT or Accessor.ComponentTypeEnum.SHORT => 2,
                Accessor.ComponentTypeEnum.FLOAT or Accessor.ComponentTypeEnum.UNSIGNED_INT => 4,
                _ => throw new NotSupportedException($"Unsupported {nameof(Accessor.ComponentTypeEnum)} {componentType}"),
            };
            return size;
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
        private static bool IsMipMapFilter(Sampler.MinFilterEnum minFilterEnum)
        {
            return minFilterEnum == Sampler.MinFilterEnum.NEAREST_MIPMAP_NEAREST ||
                   minFilterEnum == Sampler.MinFilterEnum.LINEAR_MIPMAP_NEAREST ||
                   minFilterEnum == Sampler.MinFilterEnum.NEAREST_MIPMAP_LINEAR ||
                   minFilterEnum == Sampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR;
        }

        private static unsafe void RunMeshOptimizations(ref GpuVertex[] meshVertices, ref Vector3[] meshVertexPositions, Span<uint> meshIndices)
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
        private static unsafe MeshMeshletsData GenerateMeshMeshletData(ReadOnlySpan<Vector3> meshVertexPositions, ReadOnlySpan<uint> meshIndices)
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

            MeshMeshletsData result;
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
