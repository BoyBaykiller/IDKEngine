using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
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
        // TODO: KHR_materials_transmission almost done, still needs texture support
        public static readonly string[] SupportedExtensions = new string[] { "KHR_materials_emissive_strength", "KHR_materials_volume", "KHR_materials_ior" };

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
            public GpuMeshletTaskCmd[] MeshTasksCmds;
            public GpuMeshlet[] Meshlets;
            public GpuMeshletInfo[] MeshletsInfo;
            public uint[] MeshletsVertexIndices;
            public byte[] MeshletsLocalIndices;

            // Vertex-rendering specific data
            public uint[] VertexIndices;
        }

        private struct GpuTextureLoadData
        {
            public ImageResult Image;
            public TextureMipMapFilter MinFilter;
            public TextureInterpolationFilter MagFilter;
            public GltfTextureWrapMode WrapS;
            public GltfTextureWrapMode WrapT;
            public SizedInternalFormat InternalFormat;
        }
        private struct MaterialDetails
        {
            public Vector3 EmissiveFactor;
            public uint BaseColorFactor;
            public float TransmissionFactor;
            public float AlphaCutoff;
            public float RoughnessFactor;
            public float MetallicFactor;
            public Vector3 Absorbance;
            public float IOR;

            public static readonly MaterialDetails Default = new MaterialDetails()
            {
                EmissiveFactor = new Vector3(0.0f),
                BaseColorFactor = Helper.CompressUR8G8B8A8(new Vector4(1.0f)),
                TransmissionFactor = 0.0f,
                AlphaCutoff = 0.5f,
                RoughnessFactor = 1.0f,
                MetallicFactor = 1.0f,
                Absorbance = new Vector3(0.0f),
                IOR = 1.0f, // by spec 1.5 IOR would be correct
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

            Model model = LoadFromFile(path, rootTransform);
            Logger.Log(Logger.LogLevel.Info, $"Loaded model {path}");

            return model;
        }

        public static Model LoadFromFile(string path, Matrix4 rootTransform)
        {
            ModelRoot gltfFile = ModelRoot.Load(path);

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
            
            GpuMaterialLoadData[] gpuMaterialsLoadData = GetGpuMaterialLoadDataFromGltf(gltfFile);
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
            foreach (Node node in gltfFile.DefaultScene.VisualChildren)
            {
                nodeStack.Push((node, rootTransform));
            }

            while (nodeStack.Count > 0)
            {
                (Node node, Matrix4 globalParentTransform) = nodeStack.Pop();

                Matrix4 localTransform = node.LocalMatrix.ToOpenTK();
                Matrix4 globalTransform = localTransform * globalParentTransform;

                foreach (Node child in node.VisualChildren)
                {
                    nodeStack.Push((child, globalTransform));
                }

                Mesh gltfMesh = node.Mesh;
                if (gltfMesh == null)
                {
                    continue;
                }
                
                for (int i = 0; i < gltfMesh.Primitives.Count; i++)
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
                    mesh.TransmissionBias = 0.0f;
                    mesh.MeshletsStart = listMeshlets.Count;
                    mesh.MeshletCount = meshMeshlets.Length;
                    mesh.IORBias = 0.0f;
                    mesh.AbsorbanceBias = new Vector3(0.0f);
                    if (gltfMeshPrimitive.Material != null)
                    {
                        bool hasNormalMap = gpuMaterialsLoadData[gltfMeshPrimitive.Material.LogicalIndex].NormalTexture.Image != null;
                        mesh.NormalMapStrength = hasNormalMap ? 1.0f : 0.0f;
                        mesh.MaterialIndex = gltfMeshPrimitive.Material.LogicalIndex;
                    }
                    else
                    {
                        GpuMaterial defaultGpuMaterial = LoadGpuMaterials(new GpuMaterialLoadData[] { GpuMaterialLoadData.Default })[0];
                        listMaterials.Add(defaultGpuMaterial);

                        mesh.NormalMapStrength = 0.0f;
                        mesh.MaterialIndex = listMaterials.Count - 1;
                    }

                    GpuMeshInstance meshInstance = new GpuMeshInstance();
                    meshInstance.ModelMatrix = globalTransform;

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

            Model model = new Model();
            model.Meshes = listMeshes.ToArray();
            model.MeshInstances = listMeshInstances.ToArray();
            model.Materials = listMaterials.ToArray();
            model.DrawCommands = listDrawCommands.ToArray();
            model.Vertices = listVertices.ToArray();
            model.VertexPositions = listVertexPositions.ToArray();
            model.VertexIndices = listIndices.ToArray();
            model.MeshTasksCmds = listMeshTasksCmd.ToArray();
            model.Meshlets = listMeshlets.ToArray();
            model.MeshletsInfo = listMeshletsInfo.ToArray();
            model.MeshletsVertexIndices = listMeshletsVertexIndices.ToArray();
            model.MeshletsLocalIndices = listMeshletsLocalIndices.ToArray();

            return model;
        }

        private static unsafe GpuMaterialLoadData[] GetGpuMaterialLoadDataFromGltf(ModelRoot gltf)
        {
            GpuMaterialLoadData[] materialsLoadData = new GpuMaterialLoadData[gltf.LogicalMaterials.Count];

            //for (int i = 0; i < materialsLoadData.Length; i++)
            //{
            //    materialsLoadData[i] = GpuMaterialLoadData.Default;
            //}
            //return materialsLoadData;

            Parallel.For(0, materialsLoadData.Length * GpuMaterialLoadData.TEXTURE_COUNT, i =>
            {
                int materialIndex = i / GpuMaterialLoadData.TEXTURE_COUNT;
                GpuMaterialLoadData.TextureType textureType = (GpuMaterialLoadData.TextureType)(i % GpuMaterialLoadData.TEXTURE_COUNT);

                Material gltfMaterial = gltf.LogicalMaterials[materialIndex];
                ref GpuMaterialLoadData materialData = ref materialsLoadData[materialIndex];

                // Let one thread load non image data
                if (textureType == GpuMaterialLoadData.TextureType.BaseColor)
                {
                    materialData.MaterialDetails = MaterialDetails.Default;
                    materialData.MaterialDetails.AlphaCutoff = gltfMaterial.AlphaCutoff;

                    if (gltf.ExtensionsUsed.Contains("KHR_materials_ior"))
                    {
                        // We could set IOR either way but I don't like the default value of 1.5
                        materialData.MaterialDetails.IOR = gltfMaterial.IndexOfRefraction;
                    }

                    MaterialChannel? emissiveChannel = gltfMaterial.FindChannel(KnownChannel.Emissive.ToString());
                    if (emissiveChannel.HasValue)
                    {
                        float emissiveStrength = 1.0f;
                        foreach (IMaterialParameter param in emissiveChannel.Value.Parameters)
                        {
                            if (param.Name == KnownProperty.EmissiveStrength.ToString())
                            {
                                emissiveStrength = (float)param.Value;
                                break;
                            }
                        }

                        materialData.MaterialDetails.EmissiveFactor = emissiveChannel.Value.Color.ToOpenTK().Xyz * emissiveStrength;
                    }

                    MaterialChannel? metallicRoughnessChannel = gltfMaterial.FindChannel(KnownChannel.MetallicRoughness.ToString());
                    if (metallicRoughnessChannel.HasValue)
                    {
                        float roughnessFactor = (float)metallicRoughnessChannel.Value.Parameters.First(x => x.Name == KnownProperty.RoughnessFactor.ToString()).Value;
                        float metallicFactor = (float)metallicRoughnessChannel.Value.Parameters.First(x => x.Name == KnownProperty.MetallicFactor.ToString()).Value;
                        materialData.MaterialDetails.RoughnessFactor = roughnessFactor;
                        materialData.MaterialDetails.MetallicFactor = metallicFactor;
                    }

                    MaterialChannel? baseColorChannel = gltfMaterial.FindChannel(KnownChannel.BaseColor.ToString());
                    if (baseColorChannel.HasValue)
                    {
                        Vector4 baseColor = baseColorChannel.Value.Color.ToOpenTK();
                        materialData.MaterialDetails.BaseColorFactor = Helper.CompressUR8G8B8A8(baseColor);
                    }

                    MaterialChannel? transmissionChannel = gltfMaterial.FindChannel(KnownChannel.Transmission.ToString());
                    if (transmissionChannel.HasValue)
                    {
                        foreach (IMaterialParameter param in transmissionChannel.Value.Parameters)
                        {
                            if (param.Name == KnownProperty.TransmissionFactor.ToString())
                            {
                                materialData.MaterialDetails.TransmissionFactor = (float)param.Value;
                                break;
                            }
                        }
                    }

                    MaterialChannel? volumeAttenuationChannel = gltfMaterial.FindChannel(KnownChannel.VolumeAttenuation.ToString());
                    if (volumeAttenuationChannel.HasValue)
                    {
                        float gltfAttenuationDistance = 100000.0f;
                        Vector3 gltfAttenuationColor = new Vector3(1.0f);
                        foreach (IMaterialParameter param in volumeAttenuationChannel.Value.Parameters)
                        {
                            if (param.Name == KnownProperty.RGB.ToString())
                            {
                                gltfAttenuationColor = ((System.Numerics.Vector3)param.Value).ToOpenTK();
                            }
                            if (param.Name == KnownProperty.AttenuationDistance.ToString())
                            {
                                // 0.0f is SharpGLTF's default
                                if ((float)param.Value != 0.0f)
                                {
                                    gltfAttenuationDistance = (float)param.Value;
                                }
                            }
                        }

                        // Source: https://github.com/DassaultSystemes-Technology/dspbr-pt/blob/e7cfa6e9aab2b99065a90694e1f58564d675c1a4/packages/lib/shader/integrator/pt.glsl#L24
                        // We can combine glTF Attenuation Color and Distance into a single Absorbance value
                        float x = -MathF.Log(gltfAttenuationColor.X) / gltfAttenuationDistance;
                        float y = -MathF.Log(gltfAttenuationColor.Y) / gltfAttenuationDistance;
                        float z = -MathF.Log(gltfAttenuationColor.Z) / gltfAttenuationDistance;
                        Vector3 absorbance = new Vector3(x, y, z);
                        materialData.MaterialDetails.Absorbance = absorbance;
                    }
                }

                materialData[textureType] = GetGLTextureLoadData(gltfMaterial, textureType);
            });


            return materialsLoadData;
        }
        private static GpuTextureLoadData GetGLTextureLoadData(Material material, GpuMaterialLoadData.TextureType textureType)
        {
            GpuTextureLoadData glTextureLoadData = new GpuTextureLoadData();
            ColorComponents imageColorComponents = ColorComponents.Default;
            MaterialChannel? materialChannel = null;
            {
                if (textureType == GpuMaterialLoadData.TextureType.BaseColor)
                {
                    glTextureLoadData.InternalFormat = SizedInternalFormat.Srgb8Alpha8;
                    //glTextureLoadData.InternalFormat = SizedInternalFormat.CompressedSrgbAlphaBptcUnorm;
                    imageColorComponents = ColorComponents.RedGreenBlueAlpha;
                    materialChannel = material.FindChannel(KnownChannel.BaseColor.ToString());
                }
                else if (textureType == GpuMaterialLoadData.TextureType.MetallicRoughness)
                {
                    glTextureLoadData.InternalFormat = SizedInternalFormat.R11fG11fB10f;
                    imageColorComponents = ColorComponents.RedGreenBlue;
                    materialChannel = material.FindChannel(KnownChannel.MetallicRoughness.ToString());
                }
                else if (textureType == GpuMaterialLoadData.TextureType.Normal)
                {
                    glTextureLoadData.InternalFormat = SizedInternalFormat.R11fG11fB10f;
                    imageColorComponents = ColorComponents.RedGreenBlue;
                    materialChannel = material.FindChannel(KnownChannel.Normal.ToString());
                }
                else if (textureType == GpuMaterialLoadData.TextureType.Emissive)
                {
                    glTextureLoadData.InternalFormat = SizedInternalFormat.CompressedSrgbAlphaBptcUnorm;
                    imageColorComponents = ColorComponents.RedGreenBlue;
                    materialChannel = material.FindChannel(KnownChannel.Emissive.ToString());
                }
            }

            GltfTexture gltfTexture = null;
            if (materialChannel.HasValue)
            {
                gltfTexture = materialChannel.Value.Texture;
            }

            if (gltfTexture == null)
            {
                return glTextureLoadData;
            }

            using Stream stream = gltfTexture.PrimaryImage.Content.Open();
            glTextureLoadData.Image = ImageResult.FromStream(stream, imageColorComponents);

            if (gltfTexture.Sampler == null)
            {
                glTextureLoadData.WrapS = GltfTextureWrapMode.REPEAT;
                glTextureLoadData.WrapT = GltfTextureWrapMode.REPEAT;
                glTextureLoadData.MinFilter = TextureMipMapFilter.LINEAR_MIPMAP_LINEAR;
                glTextureLoadData.MagFilter = TextureInterpolationFilter.LINEAR;
            }
            else
            {
                glTextureLoadData.WrapT = gltfTexture.Sampler.WrapT;
                glTextureLoadData.WrapS = gltfTexture.Sampler.WrapS;
                glTextureLoadData.MinFilter = gltfTexture.Sampler.MinFilter;
                glTextureLoadData.MagFilter = gltfTexture.Sampler.MagFilter;
                if (gltfTexture.Sampler.MinFilter == TextureMipMapFilter.DEFAULT)
                {
                    glTextureLoadData.MinFilter = TextureMipMapFilter.LINEAR_MIPMAP_LINEAR;
                }
                if (gltfTexture.Sampler.MagFilter == TextureInterpolationFilter.DEFAULT)
                {
                    glTextureLoadData.MagFilter = TextureInterpolationFilter.LINEAR;
                }
            }

            return glTextureLoadData;
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
                gpuMaterial.TransmissionFactor = materialLoadData.MaterialDetails.TransmissionFactor;
                gpuMaterial.AlphaCutoff = materialLoadData.MaterialDetails.AlphaCutoff;
                gpuMaterial.RoughnessFactor = materialLoadData.MaterialDetails.RoughnessFactor;
                gpuMaterial.MetallicFactor = materialLoadData.MaterialDetails.MetallicFactor;
                gpuMaterial.Absorbance = materialLoadData.MaterialDetails.Absorbance;
                gpuMaterial.IOR = materialLoadData.MaterialDetails.IOR;

                gpuMaterial.BaseColorTextureHandle = defaultTextureHandle;
                gpuMaterial.MetallicRoughnessTextureHandle = defaultTextureHandle;
                gpuMaterial.NormalTextureHandle = defaultTextureHandle;
                gpuMaterial.EmissiveTextureHandle = defaultTextureHandle;
                
                if (materialLoadData.BaseColorTexture.Image != null)
                {
                    GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                    SamplerObject sampler = new SamplerObject();
                    ConfigureGLTextureAndSampler(materialLoadData.BaseColorTexture, texture, sampler);
                    gpuMaterial.BaseColorTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                if (materialLoadData.MetallicRoughnessTexture.Image != null)
                {
                    GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                    SamplerObject sampler = new SamplerObject();
                    ConfigureGLTextureAndSampler(materialLoadData.MetallicRoughnessTexture, texture, sampler);
                    texture.SetSwizzleR(All.Blue); // "Move" metallic from Blue into Red channel
                    gpuMaterial.MetallicRoughnessTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                if (materialLoadData.NormalTexture.Image != null)
                {
                    GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                    SamplerObject sampler = new SamplerObject();
                    ConfigureGLTextureAndSampler(materialLoadData.NormalTexture, texture, sampler);
                    gpuMaterial.NormalTextureHandle = texture.GetTextureHandleARB(sampler);
                }
                if (materialLoadData.EmissiveTexture.Image != null)
                {
                    GLTexture texture = new GLTexture(TextureTarget2d.Texture2D);
                    SamplerObject sampler = new SamplerObject();
                    ConfigureGLTextureAndSampler(materialLoadData.EmissiveTexture, texture, sampler);
                    gpuMaterial.EmissiveTextureHandle = texture.GetTextureHandleARB(sampler);
                }
            }

            return materials;
        }
        private static void ConfigureGLTextureAndSampler(GpuTextureLoadData configuration, GLTexture texture, SamplerObject sampler)
        {
            bool mipmapsRequired = IsMipMapFilter(configuration.MinFilter);
            int levels = mipmapsRequired ? Math.Max(GLTexture.GetMaxMipmapLevel(configuration.Image.Width, configuration.Image.Height, 1), 1) : 1;
            texture.ImmutableAllocate(configuration.Image.Width, configuration.Image.Height, 1, configuration.InternalFormat, levels);
            texture.SubTexture2D(configuration.Image.Width, configuration.Image.Height, ColorComponentsToPixelFormat(configuration.Image.Comp), PixelType.UnsignedByte, configuration.Image.Data);

            if (mipmapsRequired)
            {
                sampler.SetSamplerParamter(SamplerParameterName.TextureMaxAnisotropyExt, 4.0f);
                texture.GenerateMipmap();
            }
            sampler.SetSamplerParamter(SamplerParameterName.TextureMinFilter, (int)configuration.MinFilter);
            sampler.SetSamplerParamter(SamplerParameterName.TextureMagFilter, (int)configuration.MagFilter);
            sampler.SetSamplerParamter(SamplerParameterName.TextureWrapS, (int)configuration.WrapS);
            sampler.SetSamplerParamter(SamplerParameterName.TextureWrapT, (int)configuration.WrapT);
        }

        private static unsafe ValueTuple<GpuVertex[], Vector3[]> LoadGpuVertexData(MeshPrimitive meshPrimitive)
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
                Span<byte> rawData = positonAccessor.SourceBufferView.Content.AsSpan(positonAccessor.ByteOffset, positonAccessor.ByteLength);
                Helper.MemCpy(rawData[0], ref vertexPositions[0], (nuint)rawData.Length);
            }

            if (hasNormals)
            {
                Span<Vector3> normals = Helper.SpanReinterpret<Vector3>(normalAccessor.SourceBufferView.Content.AsSpan(normalAccessor.ByteOffset, normalAccessor.ByteLength));
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
                Span<Vector2> texCoords = Helper.SpanReinterpret<Vector2>(texCoordAccessor.SourceBufferView.Content.AsSpan(texCoordAccessor.ByteOffset, texCoordAccessor.ByteLength));
                for (int i = 0; i < texCoords.Length; i++)
                {
                    vertices[i].TexCoord = texCoords[i];
                }
            }

            return (vertices, vertexPositions);
        }
        private static unsafe uint[] LoadGpuIndexData(MeshPrimitive meshPrimitive)
        {
            Accessor accessor = meshPrimitive.IndexAccessor;
            uint[] vertexIndices = new uint[accessor.Count];
            int componentSize = ComponentTypeToSize(accessor.Encoding);

            Span<byte> rawData = accessor.SourceBufferView.Content.AsSpan(accessor.ByteOffset, accessor.ByteLength);
            for (int i = 0; i < accessor.Count; i++)
            {
                Helper.MemCpy(rawData[componentSize * i], ref vertexIndices[i], (nuint)componentSize);
            }

            return vertexIndices;
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
