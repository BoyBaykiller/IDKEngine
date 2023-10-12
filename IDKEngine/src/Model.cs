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

            public GpuTextureLoadData BaseColorTexture;
            public GpuTextureLoadData MetallicRoughnessTexture;
            public GpuTextureLoadData NormalTexture;
            public GpuTextureLoadData EmissiveTexture;
            public MaterialDetails MaterialDetail;

            public static readonly GpuMaterialLoadData Default = new GpuMaterialLoadData()
            {
                MaterialDetail = MaterialDetails.Default,
            };
        }

        public GpuMesh[] Meshes;
        public GpuMeshInstance[] MeshInstances;
        public GpuMaterial[] Materials;
        public GpuDrawElementsCmd[] DrawCommands;
        public GpuDrawVertex[] Vertices;
        public uint[] Indices;

        private Gltf gltfModel;
        private string RootDir;
        public Model(string path)
            : this(path, Matrix4.Identity)
        {
        }

        public Model(string path, Matrix4 rootTransform)
        {
            Meshes = Array.Empty<GpuMesh>();
            MeshInstances = Array.Empty<GpuMeshInstance>();
            Materials = Array.Empty<GpuMaterial>();
            DrawCommands = Array.Empty<GpuDrawElementsCmd>();
            Vertices = Array.Empty<GpuDrawVertex>();
            Indices = Array.Empty<uint>();

            if (!File.Exists(path))
            {
                Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
                return;
            }

            LoadFromFile(path, rootTransform);
            Logger.Log(Logger.LogLevel.Info, $"Loaded model {path}");
        }


        public void LoadFromFile(string path, Matrix4 rootTransform)
        {
            gltfModel = Interface.LoadModel(path);
            
            RootDir = Path.GetDirectoryName(path);

            GpuMaterialLoadData[] glMaterialsLoadData = GetGpuMaterialLoadDataFromGltf(gltfModel.Materials, gltfModel.Textures);
            List<GpuMaterial> materials = new List<GpuMaterial>(LoadGpuMaterials(glMaterialsLoadData));
            List<GpuMesh> meshes = new List<GpuMesh>();
            List<GpuMeshInstance> meshInstances = new List<GpuMeshInstance>();
            List<GpuDrawElementsCmd> drawCommands = new List<GpuDrawElementsCmd>();
            List<GpuDrawVertex> vertices = new List<GpuDrawVertex>();
            List<uint> indices = new List<uint>();

            Stack<ValueTuple<Node, Matrix4>> nodeStack = new Stack<ValueTuple<Node, Matrix4>>();
            for (int i = 0; i < gltfModel.Scenes[0].Nodes.Length; i++)
            {
                Node node = gltfModel.Nodes[gltfModel.Scenes[0].Nodes[i]];
                nodeStack.Push((node, rootTransform));
            }

            while (nodeStack.Count > 0)
            {
                (Node node, Matrix4 globalParentTransform) = nodeStack.Pop();
                Matrix4 localTransform = NodeGetModelMatrix(node);
                Matrix4 globalTransform = localTransform * globalParentTransform;

                if (node.Children != null)
                {
                    for (int i = 0; i < node.Children.Length; i++)
                    {
                        Node childNode = gltfModel.Nodes[node.Children[i]];
                        nodeStack.Push(new ValueTuple<Node, Matrix4>(childNode, globalTransform));
                    }
                }

                if (node.Mesh.HasValue)
                {
                    Mesh gltfMesh = gltfModel.Meshes[node.Mesh.Value];
                    for (int i = 0; i < gltfMesh.Primitives.Length; i++)
                    {
                        MeshPrimitive gltfMeshPrimitive = gltfMesh.Primitives[i];
                        
                        GpuMesh mesh = new GpuMesh();
                        mesh.InstanceCount = 1;
                        mesh.EmissiveBias = 0.0f;
                        mesh.SpecularBias = 0.0f;
                        mesh.RoughnessBias = 0.0f;
                        mesh.RefractionChance = 0.0f;
                        mesh.IOR = 1.0f;
                        mesh.Absorbance = new Vector3(0.0f);
                        if (gltfMeshPrimitive.Material.HasValue)
                        {
                            bool hasNormalMap = glMaterialsLoadData[gltfMeshPrimitive.Material.Value].NormalTexture.Image != null;
                            mesh.NormalMapStrength = hasNormalMap ? 1.0f : 0.0f;
                            mesh.MaterialIndex = gltfMeshPrimitive.Material.Value;
                        }
                        else
                        {
                            GpuMaterial defaultGpuMaterial = LoadGpuMaterials(new GpuMaterialLoadData[] { GpuMaterialLoadData.Default })[0];
                            materials.Add(defaultGpuMaterial);

                            mesh.NormalMapStrength = 0.0f;
                            mesh.MaterialIndex = materials.Count - 1;
                        }

                        GpuDrawVertex[] meshVertices = LoadGpuVertexData(gltfMeshPrimitive);
                        uint[] meshIndices = LoadGpuIndexData(gltfMeshPrimitive);

                        GpuDrawElementsCmd drawCmd = new GpuDrawElementsCmd();
                        drawCmd.Count = meshIndices.Length;
                        drawCmd.InstanceCount = mesh.InstanceCount;
                        drawCmd.FirstIndex = indices.Count;
                        drawCmd.BaseVertex = vertices.Count;
                        drawCmd.BaseInstance = drawCommands.Count;

                        GpuMeshInstance meshInstance = new GpuMeshInstance();
                        meshInstance.ModelMatrix = globalTransform;

                        vertices.AddRange(meshVertices);
                        indices.AddRange(meshIndices);
                        meshes.Add(mesh);
                        meshInstances.Add(meshInstance);
                        drawCommands.Add(drawCmd);
                    }
                }
            }

            Meshes = meshes.ToArray();
            MeshInstances = meshInstances.ToArray();
            Materials = materials.ToArray();
            DrawCommands = drawCommands.ToArray();
            Vertices = vertices.ToArray();
            Indices = indices.ToArray();
        }

        private unsafe GpuMaterialLoadData[] GetGpuMaterialLoadDataFromGltf(Material[] materials, GltfTexture[] textures)
        {
            if (materials == null)
            {
                return Array.Empty<GpuMaterialLoadData>();
            }
            GpuMaterialLoadData[] materialsLoadData = new GpuMaterialLoadData[materials.Length];         
            
            Parallel.For(0, materialsLoadData.Length * GpuMaterialLoadData.TEXTURE_COUNT, i =>
            {
                int materialIndex = i / GpuMaterialLoadData.TEXTURE_COUNT;
                GpuMaterialLoadData.TextureType textureType = (GpuMaterialLoadData.TextureType)(i % GpuMaterialLoadData.TEXTURE_COUNT);

                Material material = materials[materialIndex];
                ref GpuMaterialLoadData materialData = ref materialsLoadData[materialIndex];
                
                // Let one thread load non image data
                if (textureType == GpuMaterialLoadData.TextureType.BaseColor)
                {
                    materialData.MaterialDetail = GpuMaterialLoadData.MaterialDetails.Default;
                    materialData.MaterialDetail.AlphaCutoff = material.AlphaCutoff;

                    if (material.EmissiveFactor != null)
                    {
                        materialData.MaterialDetail.EmissiveFactor = new Vector3(
                            material.EmissiveFactor[0],
                            material.EmissiveFactor[1],
                            material.EmissiveFactor[2]);
                    }
                    if (material.PbrMetallicRoughness != null)
                    {
                        if (material.PbrMetallicRoughness.BaseColorFactor != null)
                        {
                            materialData.MaterialDetail.BaseColorFactor = Helper.CompressUR8G8B8A8(new Vector4(
                                material.PbrMetallicRoughness.BaseColorFactor[0],
                                material.PbrMetallicRoughness.BaseColorFactor[1],
                                material.PbrMetallicRoughness.BaseColorFactor[2],
                                material.PbrMetallicRoughness.BaseColorFactor[3]));
                        }

                        materialData.MaterialDetail.RoughnessFactor = material.PbrMetallicRoughness.RoughnessFactor;
                        materialData.MaterialDetail.MetallicFactor = material.PbrMetallicRoughness.MetallicFactor;
                    }
                }

                materialData[textureType] = GetGLTextureLoadData(material, textures, textureType);
            });

            GpuTextureLoadData GetGLTextureLoadData(Material material, GltfTexture[] textures, GpuMaterialLoadData.TextureType textureType)
            {
                GpuTextureLoadData glTextureData = new GpuTextureLoadData();

                GltfTexture gltfTextureToLoad = null;
                ColorComponents colorComponentsToLoad = ColorComponents.RedGreenBlueAlpha;
                {
                    if (textureType == GpuMaterialLoadData.TextureType.BaseColor)
                    {
                        glTextureData.InternalFormat = SizedInternalFormat.Srgb8Alpha8;
                        colorComponentsToLoad = ColorComponents.RedGreenBlueAlpha;

                        TextureInfo textureInfo = null;
                        MaterialPbrMetallicRoughness materialPbrMetallicRoughness = material.PbrMetallicRoughness;
                        if (materialPbrMetallicRoughness != null) textureInfo = materialPbrMetallicRoughness.BaseColorTexture;
                        if (textureInfo != null) gltfTextureToLoad = textures[textureInfo.Index];
                    }
                    else if (textureType == GpuMaterialLoadData.TextureType.MetallicRoughness)
                    {
                        glTextureData.InternalFormat = SizedInternalFormat.Rgb8;
                        colorComponentsToLoad = ColorComponents.RedGreenBlue;

                        TextureInfo textureInfo = null;
                        MaterialPbrMetallicRoughness materialPbrMetallicRoughness = material.PbrMetallicRoughness;
                        if (materialPbrMetallicRoughness != null) textureInfo = materialPbrMetallicRoughness.MetallicRoughnessTexture;
                        if (textureInfo != null) gltfTextureToLoad = textures[textureInfo.Index];
                    }
                    else if (textureType == GpuMaterialLoadData.TextureType.Normal)
                    {
                        glTextureData.InternalFormat = SizedInternalFormat.R11fG11fB10f;
                        colorComponentsToLoad = ColorComponents.RedGreenBlue;

                        MaterialNormalTextureInfo textureInfo = material.NormalTexture;
                        if (textureInfo != null) gltfTextureToLoad = textures[textureInfo.Index];
                    }
                    else if (textureType == GpuMaterialLoadData.TextureType.Emissive)
                    {
                        glTextureData.InternalFormat = (SizedInternalFormat)PixelInternalFormat.CompressedSrgbAlphaBptcUnorm;
                        colorComponentsToLoad = ColorComponents.RedGreenBlue;

                        TextureInfo textureInfo = material.EmissiveTexture;
                        if (textureInfo != null) gltfTextureToLoad = gltfModel.Textures[textureInfo.Index];
                    }
                }

                {
                    bool shouldReportMissingTexture = textureType == GpuMaterialLoadData.TextureType.BaseColor ||
                                                      textureType == GpuMaterialLoadData.TextureType.MetallicRoughness ||
                                                      textureType == GpuMaterialLoadData.TextureType.Normal;
                    if (shouldReportMissingTexture && (gltfTextureToLoad == null || !gltfTextureToLoad.Source.HasValue))
                    {
                        Logger.Log(Logger.LogLevel.Warn, $"Material {material.Name} has no texture of type {textureType}");
                    }
                }

                if (gltfTextureToLoad == null)
                {
                    return glTextureData; 
                }

                if (gltfTextureToLoad.Source.HasValue)
                {
                    Image image = gltfModel.Images[gltfTextureToLoad.Source.Value];
                    string imagePath = Path.Combine(RootDir, image.Uri);

                    if (!File.Exists(imagePath))
                    {
                        Logger.Log(Logger.LogLevel.Error, $"Image \"{imagePath}\" is not found");
                        return glTextureData;
                    }

                    using FileStream stream = File.OpenRead(imagePath);
                    glTextureData.Image = ImageResult.FromStream(stream, colorComponentsToLoad);
                }
                
                if (gltfTextureToLoad.Sampler.HasValue)
                {
                    glTextureData.Sampler = gltfModel.Samplers[gltfTextureToLoad.Sampler.Value];
                }

                return glTextureData;
            }

            return materialsLoadData;
        }
        private static GpuMaterial[] LoadGpuMaterials(ReadOnlySpan<GpuMaterialLoadData> materialsLoadInfo)
        {
            GpuMaterial[] materials = new GpuMaterial[materialsLoadInfo.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                ref GpuMaterial gpuMaterial = ref materials[i];
                ref readonly GpuMaterialLoadData materialLoadData = ref materialsLoadInfo[i];

                gpuMaterial.EmissiveFactor = materialLoadData.MaterialDetail.EmissiveFactor;
                gpuMaterial.BaseColorFactor = materialLoadData.MaterialDetail.BaseColorFactor;
                gpuMaterial.RoughnessFactor = materialLoadData.MaterialDetail.RoughnessFactor;
                gpuMaterial.MetallicFactor = materialLoadData.MaterialDetail.MetallicFactor;
                gpuMaterial.AlphaCutoff = materialLoadData.MaterialDetail.AlphaCutoff;

                {
                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(materialLoadData.BaseColorTexture);
                    gpuMaterial.BaseColorTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                {
                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(materialLoadData.MetallicRoughnessTexture);
                    texture.SetSwizzleR(All.Blue); // "Move" metallic from Blue into Red channel
                    gpuMaterial.MetallicRoughnessTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                {
                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(materialLoadData.NormalTexture);
                    gpuMaterial.NormalTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                {
                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(materialLoadData.EmissiveTexture);
                    gpuMaterial.EmissiveTextureHandle = texture.GetTextureHandleARB(sampler);
                }

                static ValueTuple<GLTexture, SamplerObject> GetGLTextureAndSampler(GpuTextureLoadData data)
                {
                    if (data.Sampler == null)
                    {
                        data.Sampler = new Sampler();
                        data.Sampler.WrapT = Sampler.WrapTEnum.REPEAT;
                        data.Sampler.WrapS = Sampler.WrapSEnum.REPEAT;
                        data.Sampler.MinFilter = null;
                        data.Sampler.MagFilter = null;
                    }

                    SamplerObject sampler = new SamplerObject();
                    GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                    if (data.Image == null)
                    {
                        if (data.Sampler.MinFilter == null) data.Sampler.MinFilter = Sampler.MinFilterEnum.NEAREST;
                        if (data.Sampler.MagFilter == null) data.Sampler.MagFilter = Sampler.MagFilterEnum.NEAREST;

                        texture.ImmutableAllocate(1, 1, 1, SizedInternalFormat.Rgba32f);
                        texture.Clear(PixelFormat.Rgba, PixelType.Float, new Vector4(1.0f));
                    }
                    else
                    {
                        if (data.Sampler.MinFilter == null) data.Sampler.MinFilter = Sampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR;
                        if (data.Sampler.MagFilter == null) data.Sampler.MagFilter = Sampler.MagFilterEnum.LINEAR;

                        bool mipmapsRequired = IsMipMapFilter(data.Sampler.MinFilter.Value);
                        
                        int levels = mipmapsRequired ? Math.Max(GLTexture.GetMaxMipmapLevel(data.Image.Width, data.Image.Height, 1), 1) : 1;
                        texture.ImmutableAllocate(data.Image.Width, data.Image.Height, 1, data.InternalFormat, levels);
                        texture.SubTexture2D(data.Image.Width, data.Image.Height, ColorComponentsToPixelFormat(data.Image.Comp), PixelType.UnsignedByte, data.Image.Data);

                        if (mipmapsRequired)
                        {
                            sampler.SetSamplerParamter(SamplerParameterName.TextureMaxAnisotropyExt, 4.0f);
                            texture.GenerateMipmap();
                        }
                    }

                    sampler.SetSamplerParamter(SamplerParameterName.TextureMinFilter, (int)data.Sampler.MinFilter);
                    sampler.SetSamplerParamter(SamplerParameterName.TextureMagFilter, (int)data.Sampler.MagFilter);
                    sampler.SetSamplerParamter(SamplerParameterName.TextureWrapS, (int)data.Sampler.WrapS);
                    sampler.SetSamplerParamter(SamplerParameterName.TextureWrapT, (int)data.Sampler.WrapT);

                    return (texture, sampler);
                }
            }

            return materials;
        }

        private unsafe GpuDrawVertex[] LoadGpuVertexData(MeshPrimitive meshPrimitive)
        {
            const string GLTF_POSITION_ATTRIBUTE = "POSITION";
            const string GLTF_NORMAL_ATTRIBUTE = "NORMAL";
            const string GLTF_TEXCOORD_0_ATTRIBUTE = "TEXCOORD_0";

            GpuDrawVertex[] vertices = null;

            Dictionary<string, int> myDict = meshPrimitive.Attributes;
            for (int j = 0; j < myDict.Count; j++)
            {
                KeyValuePair<string, int> item = myDict.ElementAt(j);

                Accessor accessor = gltfModel.Accessors[item.Value];
                BufferView bufferView = gltfModel.BufferViews[accessor.BufferView.Value];
                glTFLoader.Schema.Buffer buffer = gltfModel.Buffers[bufferView.Buffer];

                using FileStream fileStream = File.OpenRead(Path.Combine(RootDir, buffer.Uri));
                fileStream.Position = accessor.ByteOffset + bufferView.ByteOffset;

                if (vertices == null)
                {
                    vertices = new GpuDrawVertex[accessor.Count];
                }

                if (item.Key == GLTF_POSITION_ATTRIBUTE)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 data;
                        Span<byte> span = new Span<byte>(&data, sizeof(Vector3));
                        fileStream.Read(span);

                        vertices[i].Position = data;
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

            return vertices;
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

        private static Matrix4 NodeGetModelMatrix(Node node)
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
            switch (componentType)
            {
                case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                case Accessor.ComponentTypeEnum.BYTE:
                    return 1;

                case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                case Accessor.ComponentTypeEnum.SHORT:
                    return 2;

                case Accessor.ComponentTypeEnum.FLOAT:
                case Accessor.ComponentTypeEnum.UNSIGNED_INT:
                    return 4;

                default:
                    Logger.Log(Logger.LogLevel.Error, $"No conversion for {componentType} into bit size is possible");
                    return 0;
            }
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
    }
}
