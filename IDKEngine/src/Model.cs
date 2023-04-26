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
using Texture = glTFLoader.Schema.Texture;

namespace IDKEngine
{
    class Model
    {
        private struct GLTextureData
        {
            public ImageResult Image;
            public Sampler Sampler;
        }

        private struct GLMaterialData
        {
            public const int TEXTURE_COUNT = 4;
            
            public enum TextureType : int
            {
                BaseColor,
                MetallicRoughness,
                Normal,
                Emissive
            }

            public ref GLTextureData this[TextureType textureType]
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

            public GLTextureData BaseColorTexture;
            public GLTextureData MetallicRoughnessTexture;
            public GLTextureData NormalTexture;
            public GLTextureData EmissiveTexture;

            public uint BaseColorFactor;
            public float MetallicFactor;
            public float RoughnessFactor;
            public Vector3 EmissiveFactor;
        }

        public GLSLMesh[] Meshes;
        public GLSLMeshInstance[] MeshInstances;
        public GLSLMaterial[] Materials;
        public GLSLDrawElementsCommand[] DrawCommands;
        public GLSLDrawVertex[] Vertices;
        public uint[] Indices;

        private Gltf gltfModel;
        public Model(string path, Matrix4 rootTransform)
        {
            LoadFromFile(path, rootTransform);
        }

        public Model(string path)
        {
            LoadFromFile(path, Matrix4.Identity);
        }

        public void LoadFromFile(string path, Matrix4 rootTransform)
        {
            if (!File.Exists(path))
            {
                Logger.Log(Logger.LogLevel.Error, $"File \"{path}\" does not exist");
                return;
            }

            gltfModel = Interface.LoadModel(path);

            string rootDir = Path.GetDirectoryName(path);

            GLMaterialData[] materialsData = LoadGLMaterialData(rootDir);
            List<GLSLMaterial> materials = new List<GLSLMaterial>(LoadGLMaterials(materialsData));

            List<GLSLMesh> meshes = new List<GLSLMesh>();
            List<GLSLMeshInstance> meshInstances = new List<GLSLMeshInstance>();
            List<GLSLDrawElementsCommand> drawCommands = new List<GLSLDrawElementsCommand>();
            List<GLSLDrawVertex> vertices = new List<GLSLDrawVertex>();
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
                        MeshPrimitive gltfPrimitive = gltfMesh.Primitives[i];
                        
                        GLSLMesh mesh = new GLSLMesh();
                        mesh.InstanceCount = 1;

                        if (gltfPrimitive.Material.HasValue)
                        {
                            mesh.MaterialIndex = gltfPrimitive.Material.Value;
                            bool hasNormalMap = materialsData[gltfPrimitive.Material.Value].NormalTexture.Image != null;
                            mesh.NormalMapStrength = hasNormalMap ? 1.0f : 0.0f;
                        }
                        else
                        {
                            GLMaterialData materialData = new GLMaterialData();
                            GLSLMaterial material = LoadGLMaterials(new GLMaterialData[] { materialData })[0];
                            materials.Add(material);

                            mesh.MaterialIndex = Materials.Length - 1;
                            mesh.NormalMapStrength = 0.0f;
                        }

                        mesh.EmissiveBias = 0.0f;
                        mesh.SpecularBias = 0.0f;
                        mesh.RoughnessBias = 0.0f;
                        mesh.RefractionChance = 0.0f;
                        mesh.IOR = 1.0f;
                        mesh.Absorbance = new Vector3(0.0f);

                        GLSLMeshInstance meshInstance = new GLSLMeshInstance();
                        meshInstance.ModelMatrix = globalTransform;

                        GLSLDrawElementsCommand cmd = new GLSLDrawElementsCommand();
                        GLSLDrawVertex[] thisVertices = LoadVertexData(rootDir, gltfMesh.Primitives[i]);
                        uint[] thisIndices = LoadIndexData(rootDir, gltfMesh.Primitives[i]);

                        cmd.BaseVertex = vertices.Count;
                        vertices.AddRange(thisVertices);

                        cmd.FirstIndex = indices.Count;
                        indices.AddRange(thisIndices);

                        cmd.Count = thisIndices.Length;
                        cmd.InstanceCount = mesh.InstanceCount;
                        cmd.BaseInstance = drawCommands.Count;


                        meshes.Add(mesh);
                        meshInstances.Add(meshInstance);
                        drawCommands.Add(cmd);
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

        private unsafe GLMaterialData[] LoadGLMaterialData(string rootDir)
        {
            GLMaterialData[] materialsLoadData = new GLMaterialData[gltfModel.Materials.Length];         
            
            Parallel.For(0, materialsLoadData.Length * GLMaterialData.TEXTURE_COUNT, i =>
            {
                int materialIndex = i / GLMaterialData.TEXTURE_COUNT;
                GLMaterialData.TextureType textureType = (GLMaterialData.TextureType)(i % GLMaterialData.TEXTURE_COUNT);

                Material material = gltfModel.Materials[materialIndex];
                ref GLMaterialData materialData = ref materialsLoadData[materialIndex];
                ref GLTextureData textureLoadData = ref materialData[textureType];
                
                // Let one thread load non image data
                if (textureType == GLMaterialData.TextureType.BaseColor)
                {
                    materialData.BaseColorFactor = Helper.CompressUNorm32Fast(new Vector4(
                        material.PbrMetallicRoughness.BaseColorFactor[0],
                        material.PbrMetallicRoughness.BaseColorFactor[1],
                        material.PbrMetallicRoughness.BaseColorFactor[2],
                        material.PbrMetallicRoughness.BaseColorFactor[3]));

                    materialData.EmissiveFactor = new Vector3(
                        material.EmissiveFactor[0],
                        material.EmissiveFactor[1],
                        material.EmissiveFactor[2]);

                    materialData.RoughnessFactor = material.PbrMetallicRoughness.RoughnessFactor;
                    materialData.MetallicFactor = material.PbrMetallicRoughness.MetallicFactor;
                }

                if (!GetData(material, textureType, out string imagePath, out textureLoadData.Sampler, out ColorComponents colorComponents))
                {
                    return;
                }

                if (!File.Exists(imagePath))
                {
                    Logger.Log(Logger.LogLevel.Error, $"Image \"{imagePath}\" is not found");
                    return;
                }

                using FileStream stream = File.OpenRead(imagePath);
                textureLoadData.Image = ImageResult.FromStream(stream, colorComponents);
            });

            bool GetData(Material material, GLMaterialData.TextureType textureType, out string imagePath, out Sampler sampler, out ColorComponents colorComponents)
            {
                sampler = null;
                imagePath = null;
                colorComponents = ColorComponents.RedGreenBlueAlpha;

                {
                    Texture texture = null;

                    if (textureType == GLMaterialData.TextureType.BaseColor)
                    {
                        TextureInfo textureInfo = material.PbrMetallicRoughness.BaseColorTexture;
                        colorComponents = ColorComponents.RedGreenBlueAlpha;
                        if (textureInfo != null) texture = gltfModel.Textures[textureInfo.Index];
                    }
                    else if (textureType == GLMaterialData.TextureType.MetallicRoughness)
                    {
                        TextureInfo textureInfo = material.PbrMetallicRoughness.MetallicRoughnessTexture;
                        colorComponents = ColorComponents.RedGreenBlue;
                        if (textureInfo != null) texture = gltfModel.Textures[textureInfo.Index];
                    }
                    else if (textureType == GLMaterialData.TextureType.Normal)
                    {
                        MaterialNormalTextureInfo textureInfo = material.NormalTexture;
                        colorComponents = ColorComponents.RedGreenBlue;
                        if (textureInfo != null) texture = gltfModel.Textures[textureInfo.Index];
                    }
                    else if (textureType == GLMaterialData.TextureType.Emissive)
                    {
                        TextureInfo textureInfo = material.EmissiveTexture;
                        colorComponents = ColorComponents.RedGreenBlue;
                        if (textureInfo != null) texture = gltfModel.Textures[textureInfo.Index];
                    }

                    if (texture != null)
                    {
                        if (texture.Source.HasValue)
                        {
                            Image image = gltfModel.Images[texture.Source.Value];
                            imagePath = Path.Combine(rootDir, image.Uri);
                        }
                        if (texture.Sampler.HasValue)
                        {
                            sampler = gltfModel.Samplers[texture.Sampler.Value];
                        }
                    }
                }

                return imagePath != null;
            }

            return materialsLoadData;
        }

        private unsafe GLSLDrawVertex[] LoadVertexData(string rootDir, MeshPrimitive meshPrimitive)
        {
            const string GLTF_POSITION_ATTRIBUTE = "POSITION";
            const string GLTF_NORMAL_ATTRIBUTE = "NORMAL";
            const string GLTF_TEXCOORD_0_ATTRIBUTE = "TEXCOORD_0";

            GLSLDrawVertex[] vertices = null;

            Dictionary<string, int> myDict = meshPrimitive.Attributes;
            for (int j = 0; j < myDict.Count; j++)
            {
                KeyValuePair<string, int> item = myDict.ElementAt(j);

                Accessor accessor = gltfModel.Accessors[item.Value];
                BufferView bufferView = gltfModel.BufferViews[accessor.BufferView.Value];
                glTFLoader.Schema.Buffer buffer = gltfModel.Buffers[bufferView.Buffer];

                using FileStream fileStream = File.OpenRead(Path.Combine(rootDir, buffer.Uri));
                fileStream.Position = accessor.ByteOffset + bufferView.ByteOffset;

                if (vertices == null)
                {
                    vertices = new GLSLDrawVertex[accessor.Count];
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
                        vertices[i].Normal = Helper.CompressSNorm32Fast(normal);

                        Vector3 c1 = Vector3.Cross(normal, Vector3.UnitZ);
                        Vector3 c2 = Vector3.Cross(normal, Vector3.UnitY);
                        Vector3 tangent = Vector3.Dot(c1, c1) > Vector3.Dot(c2, c2) ? c1 : c2;
                        vertices[i].Tangent = Helper.CompressSNorm32Fast(tangent);
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
        
        private unsafe uint[] LoadIndexData(string rootDir, MeshPrimitive meshPrimitive)
        {
            Accessor accessor = gltfModel.Accessors[meshPrimitive.Indices.Value];
            BufferView bufferView = gltfModel.BufferViews[accessor.BufferView.Value];
            glTFLoader.Schema.Buffer buffer = gltfModel.Buffers[bufferView.Buffer];

            using FileStream fileStream = File.OpenRead(Path.Combine(rootDir, buffer.Uri));
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

        private static GLSLMaterial[] LoadGLMaterials(GLMaterialData[] materialsLoadInfo)
        {
            GLSLMaterial[] materials = new GLSLMaterial[materialsLoadInfo.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                ref GLSLMaterial glslMaterial = ref materials[i];
                GLMaterialData materialLoadData = materialsLoadInfo[i];

                glslMaterial.BaseColorFactor = materialLoadData.BaseColorFactor;
                glslMaterial.RoughnessFactor = materialLoadData.RoughnessFactor;
                glslMaterial.MetallicFactor = materialLoadData.MetallicFactor;
                glslMaterial.EmissiveFactor = materialLoadData.EmissiveFactor;

                {
                    GLTextureData loadData = materialLoadData.BaseColorTexture;
                    SizedInternalFormat internalFormat = SizedInternalFormat.Srgb8Alpha8;

                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(loadData, internalFormat);
                    glslMaterial.BaseColorTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                {
                    GLTextureData loadData = materialLoadData.MetallicRoughnessTexture;
                    SizedInternalFormat internalFormat = SizedInternalFormat.Rgb8;

                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(loadData, internalFormat);
                    // "Move" metallic from Blue into Red channel
                    texture.SetSwizzleR(All.Blue);

                    glslMaterial.MetallicRoughnessTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                {
                    GLTextureData loadData = materialLoadData.NormalTexture;
                    SizedInternalFormat internalFormat = SizedInternalFormat.R11fG11fB10f;

                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(loadData, internalFormat);
                    glslMaterial.NormalTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                {
                    GLTextureData loadData = materialLoadData.EmissiveTexture;
                    SizedInternalFormat internalFormat = SizedInternalFormat.Srgb8Alpha8;

                    (GLTexture texture, SamplerObject sampler) = GetGLTextureAndSampler(loadData, internalFormat);
                    glslMaterial.EmissiveTextureHandle = texture.GetTextureHandleARB(sampler);
                }

                static ValueTuple<GLTexture, SamplerObject> GetGLTextureAndSampler(GLTextureData data, SizedInternalFormat internalFormat)
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

                        texture.ImmutableAllocate(1, 1, 1, internalFormat);
                        texture.Clear(PixelFormat.Rgba, PixelType.Float, new Vector4(1.0f));
                    }
                    else
                    {
                        if (data.Sampler.MinFilter == null) data.Sampler.MinFilter = Sampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR;
                        if (data.Sampler.MagFilter == null) data.Sampler.MagFilter = Sampler.MagFilterEnum.LINEAR;

                        bool mipmapsRequired = IsMipMapFilter(data.Sampler.MinFilter.Value);
                        
                        int levels = mipmapsRequired ? Math.Max(GLTexture.GetMaxMipmapLevel(data.Image.Width, data.Image.Height, 1), 1) : 1;
                        texture.ImmutableAllocate(data.Image.Width, data.Image.Height, 1, internalFormat, levels);
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
