using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using StbImageSharp;
using glTFLoader;
using glTFLoader.Schema;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using Texture = IDKEngine.Render.Objects.Texture;
using GLTFTexture = glTFLoader.Schema.Texture;

namespace IDKEngine
{
    class Model
    {
        private struct MaterialData
        {
            public ImageResult Image;
            public Sampler Sampler;
        }

        public GLSLMesh[] Meshes;
        public GLSLMeshInstance[] MeshInstances;
        public GLSLMaterial[] Materials;
        public GLSLDrawElementsCommand[] DrawCommands;
        public GLSLDrawVertex[] Vertices;
        public uint[] Indices;

        private Gltf model;
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
            model = Interface.LoadModel(path);

            string rootDir = Path.GetDirectoryName(path);

            MaterialData[] materialData = LoadMaterialData(rootDir);
            Materials = LoadMaterials(materialData);

            List<GLSLMesh> meshes = new List<GLSLMesh>();
            List<GLSLMeshInstance> meshInstances = new List<GLSLMeshInstance>();
            List<GLSLDrawElementsCommand> drawCommands = new List<GLSLDrawElementsCommand>();
            List<GLSLDrawVertex> vertices = new List<GLSLDrawVertex>();
            List<uint> indices = new List<uint>();

            Stack<Tuple<Node, Matrix4>> nodeStack = new Stack<Tuple<Node, Matrix4>>();
            for (int i = 0; i < model.Scenes[0].Nodes.Length; i++)
            {
                Node node = model.Nodes[model.Scenes[0].Nodes[i]];
                nodeStack.Push(new Tuple<Node, Matrix4>(node, rootTransform));
            }

            while (nodeStack.Count > 0)
            {
                Tuple<Node, Matrix4> tuple = nodeStack.Pop();
                Node node = tuple.Item1;
                Matrix4 globalParentTransform = tuple.Item2;

                Matrix4 localTransform = NodeToMat4(node);
                Matrix4 globalTransform = localTransform * globalParentTransform;

                if (node.Children != null)
                {
                    for (int i = 0; i < node.Children.Length; i++)
                    {
                        Node childNode = model.Nodes[node.Children[i]];
                        nodeStack.Push(new Tuple<Node, Matrix4>(childNode, globalTransform));
                    }
                }

                if (node.Mesh.HasValue)
                {
                    Mesh gltfMesh = model.Meshes[node.Mesh.Value];
                    for (int i = 0; i < gltfMesh.Primitives.Length; i++)
                    {
                        MeshPrimitive gltfPrimitive = gltfMesh.Primitives[i];

                        GLSLMesh mesh = new GLSLMesh();
                        mesh.InstanceCount = 1;
                        mesh.MaterialIndex = gltfPrimitive.Material.Value;

                        bool hasNormalMap = materialData[gltfPrimitive.Material.Value * GLSLMaterial.TEXTURE_COUNT + 2].Image != null;
                        mesh.NormalMapStrength = hasNormalMap ? 1.0f : 0.0f;

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
            DrawCommands = drawCommands.ToArray();
            Vertices = vertices.ToArray();
            Indices = indices.ToArray();
        }

        private GLSLMaterial[] LoadMaterials(MaterialData[] loadData)
        {
            GLSLMaterial[] materials = new GLSLMaterial[model.Materials.Length];

            for (int i = 0; i < materials.Length; i++)
            {
                ref GLSLMaterial glslMaterial = ref materials[i];

                Material material = model.Materials[i];

                glslMaterial.BaseColorFactor = Helper.CompressUNorm32Fast(new Vector4(
                    material.PbrMetallicRoughness.BaseColorFactor[0],
                    material.PbrMetallicRoughness.BaseColorFactor[1],
                    material.PbrMetallicRoughness.BaseColorFactor[2],
                    material.PbrMetallicRoughness.BaseColorFactor[3]));

                glslMaterial.EmissiveFactor = new Vector3(
                        material.EmissiveFactor[0],
                        material.EmissiveFactor[1],
                        material.EmissiveFactor[2]);

                glslMaterial.RoughnessFactor = material.PbrMetallicRoughness.RoughnessFactor;
                glslMaterial.MetallicFactor = material.PbrMetallicRoughness.MetallicFactor;

                {
                    MaterialData data = loadData[i * GLSLMaterial.TEXTURE_COUNT + 0];
                    SizedInternalFormat internalFormat = SizedInternalFormat.Srgb8Alpha8;
                    glslMaterial.BaseColorTextureHandle = data.Image != null ? GetTextureHandle(data, internalFormat) : GetFallbackTextureHandle(internalFormat);
                }
                {
                    MaterialData data = loadData[i * GLSLMaterial.TEXTURE_COUNT + 1];
                    SizedInternalFormat internalFormat = SizedInternalFormat.Rg8;
                    glslMaterial.MetallicRoughnessTextureHandle = data.Image != null ? GetTextureHandle(data, internalFormat) : GetFallbackTextureHandle(internalFormat);
                }
                {
                    MaterialData data = loadData[i * GLSLMaterial.TEXTURE_COUNT + 2];
                    SizedInternalFormat internalFormat = SizedInternalFormat.R11fG11fB10f;
                    glslMaterial.NormalTextureHandle = data.Image != null ? GetTextureHandle(data, internalFormat) : GetFallbackTextureHandle(internalFormat);
                }
                {
                    MaterialData data = loadData[i * GLSLMaterial.TEXTURE_COUNT + 3];
                    SizedInternalFormat internalFormat = SizedInternalFormat.Srgb8Alpha8;
                    glslMaterial.EmissiveTextureHandle = data.Image != null ? GetTextureHandle(data, internalFormat) : GetFallbackTextureHandle(internalFormat);
                }

                static ulong GetTextureHandle(MaterialData data, SizedInternalFormat internalFormat)
                {
                    SamplerObject glSampler = new SamplerObject();
                    glSampler.SetSamplerParamter(SamplerParameterName.TextureMinFilter, (int)data.Sampler.MinFilter);
                    glSampler.SetSamplerParamter(SamplerParameterName.TextureMagFilter, (int)data.Sampler.MagFilter);
                    glSampler.SetSamplerParamter(SamplerParameterName.TextureWrapS, (int)data.Sampler.WrapS);
                    glSampler.SetSamplerParamter(SamplerParameterName.TextureWrapT, (int)data.Sampler.WrapT);
                    glSampler.SetSamplerParamter(SamplerParameterName.TextureMaxAnisotropyExt, 4.0f);

                    Texture glTexture = new Texture(TextureTarget2d.Texture2D);
                    glTexture.ImmutableAllocate(data.Image.Width, data.Image.Height, 1, internalFormat, Math.Max(Texture.GetMaxMipmapLevel(data.Image.Width, data.Image.Height, 1), 1));
                    glTexture.SubTexture2D(data.Image.Width, data.Image.Height, PixelFormat.Rgba, PixelType.UnsignedByte, data.Image.Data);
                    glTexture.GenerateMipmap();

                    return glTexture.GetTextureSamplerHandleARB(glSampler);
                }
            }

            return materials;
        }
        private MaterialData[] LoadMaterialData(string rootDir)
        {
            MaterialData[] localMaterialData = new MaterialData[model.Materials.Length * GLSLMaterial.TEXTURE_COUNT];
            
            Sampler.MagFilterEnum magFilterDefault = Sampler.MagFilterEnum.LINEAR;
            Sampler.MinFilterEnum minFilterDefault = Sampler.MinFilterEnum.LINEAR_MIPMAP_LINEAR;
            Parallel.For(0, localMaterialData.Length, i =>
            {
                int materialIndex = i / GLSLMaterial.TEXTURE_COUNT;
                int imageIndex = i % GLSLMaterial.TEXTURE_COUNT;

                Material material = model.Materials[materialIndex];

                Sampler sampler = null;
                string imagePath = null;

                if (imageIndex == 0)
                {
                    TextureInfo textureInfo = material.PbrMetallicRoughness.BaseColorTexture;
                    if (textureInfo != null)
                    {
                        GetImagePathAndSampler(model.Textures[textureInfo.Index], out imagePath, out sampler);
                    }
                }
                else if (imageIndex == 1)
                {
                    TextureInfo textureInfo = material.PbrMetallicRoughness.MetallicRoughnessTexture;
                    if (textureInfo != null)
                    {
                        GetImagePathAndSampler(model.Textures[textureInfo.Index], out imagePath, out sampler);
                    }
                }
                else if (imageIndex == 2)
                {
                    MaterialNormalTextureInfo textureInfo = material.NormalTexture;
                    if (textureInfo != null)
                    {
                        GetImagePathAndSampler(model.Textures[textureInfo.Index], out imagePath, out sampler);

                    }
                }
                else if (imageIndex == 3)
                {
                    TextureInfo textureInfo = material.EmissiveTexture;
                    if (textureInfo != null)
                    {
                        GetImagePathAndSampler(model.Textures[textureInfo.Index], out imagePath, out sampler);
                    }
                }

                void GetImagePathAndSampler(GLTFTexture gltfTexture, out string imagePath, out Sampler sampler)
                {

                    if (gltfTexture.Source.HasValue)
                    {
                        Image image = model.Images[gltfTexture.Source.Value];
                        imagePath = Path.Combine(rootDir, image.Uri);
                    }
                    else
                    {
                        imagePath = null;
                    }

                    if (gltfTexture.Sampler.HasValue)
                    {
                        sampler = model.Samplers[gltfTexture.Sampler.Value];
                    }
                    else
                    {
                        sampler = null;
                    }
                }

                if (imagePath == null)
                {
                    return;
                }

                if (!File.Exists(imagePath))
                {
                    Logger.Log(Logger.LogLevel.Error, $"Image \"{imagePath}\" is not found");
                    return;
                }
                
                
                if (sampler == null)
                {
                    sampler = new Sampler();
                    sampler.WrapS = Sampler.WrapSEnum.REPEAT;
                    sampler.WrapT = Sampler.WrapTEnum.REPEAT;
                    sampler.MagFilter = magFilterDefault;
                    sampler.MinFilter = minFilterDefault;
                }

                if (sampler.MinFilter == null)
                {
                    sampler.MinFilter = minFilterDefault;
                }
                if (sampler.MagFilter == null)
                {
                    sampler.MagFilter = magFilterDefault;
                }

                using FileStream stream = File.OpenRead(imagePath);
                localMaterialData[i].Image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                localMaterialData[i].Sampler = sampler;
            });
            return localMaterialData;
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

                Accessor accessor = model.Accessors[item.Value];
                BufferView bufferView = model.BufferViews[accessor.BufferView.Value];
                glTFLoader.Schema.Buffer buffer = model.Buffers[bufferView.Buffer];

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
            Accessor accessor = model.Accessors[meshPrimitive.Indices.Value];
            BufferView bufferView = model.BufferViews[accessor.BufferView.Value];
            glTFLoader.Schema.Buffer buffer = model.Buffers[bufferView.Buffer];

            using FileStream fileStream = File.OpenRead(Path.Combine(rootDir, buffer.Uri));
            fileStream.Position = accessor.ByteOffset + bufferView.ByteOffset;

            uint[] indices = new uint[accessor.Count];
            int componentSize = ComponentTypeToSizeInBits(accessor.ComponentType);
            for (int j = 0; j < accessor.Count; j++)
            {
                uint data;
                Span<byte> span = new Span<byte>(&data, componentSize / 8);
                fileStream.Read(span);

                indices[j] = data;
            }

            return indices;
        }

        private static ulong GetFallbackTextureHandle(SizedInternalFormat internalFormat)
        {
            Texture glTexture = new Texture(TextureTarget2d.Texture2D);
            glTexture.ImmutableAllocate(1, 1, 1, internalFormat);
            glTexture.Clear(PixelFormat.Red, PixelType.Float, 1.0f);
            glTexture.SetSwizzle(All.Red, All.Red, All.Red, All.Red);
            glTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            glTexture.SetWrapMode(TextureWrapMode.Repeat, TextureWrapMode.Repeat);
            return glTexture.GetTextureHandleARB();
        }

        private static int ComponentTypeToSizeInBits(Accessor.ComponentTypeEnum componentType)
        {
            switch (componentType)
            {
                case Accessor.ComponentTypeEnum.UNSIGNED_BYTE:
                case Accessor.ComponentTypeEnum.BYTE:
                    return 8;

                case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                case Accessor.ComponentTypeEnum.SHORT:
                    return 16;

                case Accessor.ComponentTypeEnum.FLOAT:
                case Accessor.ComponentTypeEnum.UNSIGNED_INT:
                    return 32;

                default:
                    Logger.Log(Logger.LogLevel.Error, $"No conversion for {componentType} was found");
                    return 0;
            }
        }
        private static Matrix4 NodeToMat4(Node node)
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
    }
}
