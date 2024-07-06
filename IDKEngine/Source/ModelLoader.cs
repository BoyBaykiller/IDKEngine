using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using Ktx;
using SharpGLTF.Schema2;
using SharpGLTF.Materials;
using Meshoptimizer;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;
using GLTexture = BBOpenGL.BBG.Texture;
using GLSampler = BBOpenGL.BBG.Sampler;
using GltfTexture = SharpGLTF.Schema2.Texture;
using GltfSampler = SharpGLTF.Schema2.TextureSampler;

namespace IDKEngine
{
    public static class ModelLoader
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

        public struct Model
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
            public BBG.DrawMeshTasksIndirectCommandNV[] MeshletTasksCmds;
            public GpuMeshlet[] Meshlets;
            public GpuMeshletInfo[] MeshletsInfo;
            public uint[] MeshletsVertexIndices;
            public byte[] MeshletsLocalIndices;
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

            public ref GltfTexture this[TextureType textureType]
            {
                get
                {
                    switch (textureType)
                    {
                        case TextureType.BaseColor: return ref Unsafe.AsRef(ref BaseColorTexture);
                        case TextureType.MetallicRoughness: return ref Unsafe.AsRef(ref MetallicRoughnessTexture);
                        case TextureType.Normal: return ref Unsafe.AsRef(ref NormalTexture);
                        case TextureType.Emissive: return ref Unsafe.AsRef(ref EmissiveTexture);
                        case TextureType.Transmission: return ref Unsafe.AsRef(ref TransmissionTexture);
                        default: throw new NotSupportedException($"Unsupported {nameof(TextureType)} {textureType}");
                    }
                }
            }

            public MaterialParams MaterialParams;

            public GltfTexture BaseColorTexture;
            public GltfTexture MetallicRoughnessTexture;
            public GltfTexture NormalTexture;
            public GltfTexture EmissiveTexture;
            public GltfTexture TransmissionTexture;

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
            public bool DoAlphaBlending;

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

        public static event Action? TextureLoaded;
        public static Model? LoadGltfFromFile(string path)
        {
            return LoadGltfFromFile(path, Matrix4.Identity);
        }

        public static Model? LoadGltfFromFile(string path, Matrix4 rootTransform)
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

            if (!CompressionUtils.IsCompressedGltf(gltf))
            {
                Logger.Log(Logger.LogLevel.Warn, $"Model \"{fileName}\" is uncompressed");
            }

            if (gltf.ExtensionsUsed.Contains("KHR_texture_basisu") && !gltf.ExtensionsUsed.Contains("IDK_BC5_normal_metallicRoughness"))
            {
                Logger.Log(Logger.LogLevel.Warn, $"Model \"{fileName}\" uses extension KHR_texture_basisu without IDK_BC5_normal_metallicRoughness,\n" +
                                                  "causing normal and metallicRoughness textures with a suboptimal format (BC7) and potentially visible error.\n" +
                                                  "Optimal compression can be done with https://github.com/BoyBaykiller/meshoptimizer");
            }

            Stopwatch sw = Stopwatch.StartNew();
            Model model = GltfToEngineFormat(gltf, rootTransform);
            sw.Stop();

            nint totalIndicesCount = 0;
            for (int i = 0; i < model.DrawCommands.Length; i++)
            {
                ref readonly BBG.DrawElementsIndirectCommand cmd = ref model.DrawCommands[i];
                totalIndicesCount += cmd.IndexCount * cmd.InstanceCount;
            }
            Logger.Log(Logger.LogLevel.Info, $"Loaded \"{fileName}\" in {sw.ElapsedMilliseconds}ms (Triangles = {totalIndicesCount / 3})");

            return model;
        }

        private static Model GltfToEngineFormat(ModelRoot gltf, Matrix4 rootTransform)
        {
            MaterialLoadData[] materialsLoadData = GetMaterialLoadDataFromGltf(gltf.LogicalMaterials);
            List<GpuMaterial> listMaterials = new List<GpuMaterial>(LoadGpuMaterials(materialsLoadData, gltf.ExtensionsUsed.Contains("IDK_BC5_normal_metallicRoughness")));

            List<GpuMesh> listMeshes = new List<GpuMesh>();
            List<GpuMeshInstance> listMeshInstances = new List<GpuMeshInstance>();
            List<BBG.DrawElementsIndirectCommand> listDrawCommands = new List<BBG.DrawElementsIndirectCommand>();
            List<GpuVertex> listVertices = new List<GpuVertex>();
            List<Vector3> listVertexPositions = new List<Vector3>();
            List<uint> listIndices = new List<uint>();
            List<BBG.DrawMeshTasksIndirectCommandNV> listMeshetTasksCmd = new List<BBG.DrawMeshTasksIndirectCommandNV>();
            List<GpuMeshlet> listMeshlets = new List<GpuMeshlet>();
            List<GpuMeshletInfo> listMeshletsInfo = new List<GpuMeshletInfo>();
            List<uint> listMeshletsVertexIndices = new List<uint>();
            List<byte> listMeshletsLocalIndices = new List<byte>();

            Stack<ValueTuple<Node, Matrix4>> nodeStack = new Stack<ValueTuple<Node, Matrix4>>();
            foreach (Node node in gltf.DefaultScene.VisualChildren)
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

                bool nodeUsesInstancing = TryLoadInstancedNodes(node, out Matrix4[] nodeTransformations);
                if (!nodeUsesInstancing)
                {
                    // If node does not define transformations using EXT_mesh_gpu_instancing extension we must use standard local transform
                    nodeTransformations = [nodeLocalTransform];
                }

                //nodeTransformations = new Matrix4[5];
                //for (int i = 0; i < nodeTransformations.Length; i++)
                //{
                //    Vector3 trans = RNG.RandomVec3(-15.0f, 15.0f);
                //    Vector3 rot = RNG.RandomVec3(0.0f, 2.0f * MathF.PI);
                //    float scale = 1.0f;
                //    var test = Matrix4.CreateScale(scale) *
                //            Matrix4.CreateRotationZ(rot.Z) *
                //               Matrix4.CreateRotationY(rot.Y) *
                //               Matrix4.CreateRotationX(rot.X) *
                //               Matrix4.CreateTranslation(trans);

                //    nodeTransformations[i] = test;
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
                    mesh.InstanceCount = nodeTransformations.Length;
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
                        bool hasNormalMap = materialsLoadData[gltfMeshPrimitive.Material.LogicalIndex].NormalTexture != null;
                        mesh.NormalMapStrength = hasNormalMap ? 1.0f : 0.0f;
                        mesh.MaterialIndex = gltfMeshPrimitive.Material.LogicalIndex;
                    }
                    else
                    {
                        GpuMaterial defaultGpuMaterial = LoadGpuMaterials([MaterialLoadData.Default])[0];
                        listMaterials.Add(defaultGpuMaterial);

                        mesh.NormalMapStrength = 0.0f;
                        mesh.MaterialIndex = listMaterials.Count - 1;
                    }

                    GpuMeshInstance[] meshInstances = new GpuMeshInstance[mesh.InstanceCount];
                    BBG.DrawMeshTasksIndirectCommandNV[] meshletTaskCmds = new BBG.DrawMeshTasksIndirectCommandNV[mesh.InstanceCount];
                    for (int j = 0; j < meshInstances.Length; j++)
                    {
                        ref GpuMeshInstance meshInstance = ref meshInstances[j];
                        meshInstance.ModelMatrix = nodeTransformations[j] * parentNodeGlobalTransform;
                        meshInstance.MeshIndex = listMeshes.Count;

                        ref BBG.DrawMeshTasksIndirectCommandNV meshletTaskCmd = ref meshletTaskCmds[j];
                        meshletTaskCmd.First = 0;
                        meshletTaskCmd.Count = (int)MathF.Ceiling(meshMeshlets.Length / 32.0f); // divide by task shader work group size
                    }

                    BBG.DrawElementsIndirectCommand drawCmd = new BBG.DrawElementsIndirectCommand();
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

        private static unsafe GpuMaterial[] LoadGpuMaterials(ReadOnlySpan<MaterialLoadData> materialsLoadData, bool useExtBc5NormalMetallicRoughness = false)
        {
            GpuMaterial[] gpuMaterials = new GpuMaterial[materialsLoadData.Length];
            for (int i = 0; i < gpuMaterials.Length; i++)
            {
                ref readonly MaterialLoadData materialLoadData = ref materialsLoadData[i];
                ref GpuMaterial gpuMaterial = ref gpuMaterials[i];

                MaterialParams materialParams = materialLoadData.MaterialParams;
                gpuMaterial.EmissiveFactor = materialParams.EmissiveFactor;
                gpuMaterial.BaseColorFactor = Compression.CompressUR8G8B8A8(materialParams.BaseColorFactor);
                gpuMaterial.TransmissionFactor = materialParams.TransmissionFactor;
                gpuMaterial.AlphaCutoff = materialParams.AlphaCutoff;
                gpuMaterial.RoughnessFactor = materialParams.RoughnessFactor;
                gpuMaterial.MetallicFactor = materialParams.MetallicFactor;
                gpuMaterial.Absorbance = materialParams.Absorbance;
                gpuMaterial.IOR = materialParams.IOR;
                gpuMaterial.DoAlphaBlending = materialParams.DoAlphaBlending;

                for (int j = 0; j < GpuMaterial.TEXTURE_COUNT; j++)
                {
                    GpuMaterial.BindlessHandle textureType = (GpuMaterial.BindlessHandle)j;
                    GltfTexture gltfTexture = materialLoadData[(MaterialLoadData.TextureType)textureType];

                    if (gltfTexture == null)
                    {
                        // By having a pure white fallback we can keep the sampling logic
                        // the same in shaders and still comply to glTF spec
                        gpuMaterial[textureType] = FallbackTextures.PureWhite();
                        continue;
                    }

                    if (TryAsyncLoadGltfTexture(gltfTexture, textureType, useExtBc5NormalMetallicRoughness, out GLTexture glTexture, out GLSampler glSampler))
                    {
                        GLTexture.BindlessHandle bindlessHandle = glTexture.GetTextureHandleARB(glSampler);
                        gpuMaterial[textureType] = bindlessHandle;
                    }
                    else
                    {
                        if (textureType == GpuMaterial.BindlessHandle.BaseColor)
                        {
                            gpuMaterial[textureType] = FallbackTextures.PurpleBlack();
                        }
                        else
                        {
                            gpuMaterial[textureType] = FallbackTextures.PureWhite();
                        }
                    }
                }
            };

            return gpuMaterials;
        }

        private static unsafe bool TryAsyncLoadGltfTexture(GltfTexture gltfTexture, GpuMaterial.BindlessHandle textureType, bool useExtBc5NormalMetallicRoughness, out GLTexture glTexture, out GLSampler glSampler)
        {
            glTexture = null;
            glSampler = null;

            Ktx2Texture ktx2Texture = null;
            ImageLoader.ImageHeader imageHeader = new ImageLoader.ImageHeader();
            GLTexture.InternalFormat internalFormat = 0;
            bool mipmapsRequired = false;
            int levels = 0;
            if (gltfTexture.PrimaryImage.Content.IsPng || gltfTexture.PrimaryImage.Content.IsJpg)
            {
                ReadOnlySpan<byte> imageData = gltfTexture.PrimaryImage.Content.Content.Span;
                if (!ImageLoader.TryGetImageHeader(imageData, out imageHeader))
                {
                    Logger.Log(Logger.LogLevel.Error, $"Error parsing header for image \"{gltfTexture.PrimaryImage.Name}\"");
                    return false;
                }
                levels = GLTexture.GetMaxMipmapLevel(imageHeader.Width, imageHeader.Height, 1);

                internalFormat = textureType switch
                {
                    GpuMaterial.BindlessHandle.BaseColor => GLTexture.InternalFormat.R8G8B8A8Srgb,
                    GpuMaterial.BindlessHandle.Emissive => GLTexture.InternalFormat.R8G8B8A8Srgb,
                    GpuMaterial.BindlessHandle.MetallicRoughness => GLTexture.InternalFormat.R11G11B10Float, // MetallicRoughnessTexture stores metalness and roughness in G and B components. Therefore need to load 3 channels :(
                    GpuMaterial.BindlessHandle.Normal => GLTexture.InternalFormat.R8G8Unorm,
                    GpuMaterial.BindlessHandle.Transmission => GLTexture.InternalFormat.R8Unorm,
                    _ => throw new NotSupportedException($"{nameof(MaterialLoadData.TextureType)} = {textureType} not supported")
                };
            }
            else if (gltfTexture.PrimaryImage.Content.IsKtx2)
            {
                ReadOnlySpan<byte> imageData = gltfTexture.PrimaryImage.Content.Content.Span;
                
                Ktx2.ErrorCode errorCode = Ktx2Texture.FromMemory(imageData, Ktx2.TextureCreateFlag.LoadImageDataBit, out ktx2Texture);
                if (errorCode != Ktx2.ErrorCode.Success)
                {
                    Logger.Log(Logger.LogLevel.Error, $"Failed to load KTX texture. {nameof(Ktx2Texture.FromMemory)} returned {errorCode}");
                    return false;
                }
                if (!ktx2Texture.NeedsTranscoding)
                {
                    Logger.Log(Logger.LogLevel.Error, "KTX textures are expected to require transcoding, meaning they are either ETC1S or UASTC encoded.\n" +
                                                        $"SupercompressionScheme = {ktx2Texture.SupercompressionScheme}");
                    return false;
                }

                imageHeader.Width = ktx2Texture.BaseWidth;
                imageHeader.Height = ktx2Texture.BaseHeight;
                imageHeader.Channels = ktx2Texture.Channels;
                levels = ktx2Texture.Levels;

                internalFormat = textureType switch
                {
                    GpuMaterial.BindlessHandle.BaseColor => GLTexture.InternalFormat.BC7RgbaSrgb,
                    GpuMaterial.BindlessHandle.Emissive => GLTexture.InternalFormat.BC7RgbaSrgb,

                    // BC5 support added with gltfpack fork (https://github.com/BoyBaykiller/meshoptimizer) implementing IDK_BC5_normal_metallicRoughness
                    GpuMaterial.BindlessHandle.MetallicRoughness => useExtBc5NormalMetallicRoughness ? GLTexture.InternalFormat.BC5RgUnorm : GLTexture.InternalFormat.BC7RgbaUnorm,
                    GpuMaterial.BindlessHandle.Normal => useExtBc5NormalMetallicRoughness ? GLTexture.InternalFormat.BC5RgUnorm : GLTexture.InternalFormat.BC7RgbaUnorm,

                    GpuMaterial.BindlessHandle.Transmission => GLTexture.InternalFormat.BC4RUnorm,
                    _ => throw new NotSupportedException($"{nameof(textureType)} = {textureType} not supported")
                };
            }
            else
            {
                Logger.Log(Logger.LogLevel.Error, $"Unsupported MimeType = {gltfTexture.PrimaryImage.Content.MimeType}");
                return false;
            }

            glSampler = new GLSampler(GetGLSamplerState(gltfTexture.Sampler));
            glTexture = new GLTexture(GLTexture.Type.Texture2D);
            if (textureType == GpuMaterial.BindlessHandle.MetallicRoughness && !useExtBc5NormalMetallicRoughness)
            {
                // By the spec "The metalness values are sampled from the B channel. The roughness values are sampled from the G channel"
                // We move metallic from B into R channel, unless IDK_BC5_normal_metallicRoughness is used where this is already standard behaviour.
                glTexture.SetSwizzleR(GLTexture.Swizzle.B);
            }

            mipmapsRequired = GLSampler.IsMipmapFilter(glSampler.State.MinFilter);
            levels = mipmapsRequired ? levels : 1;
            glTexture.ImmutableAllocate(imageHeader.Width, imageHeader.Height, 1, internalFormat, levels);

            GLTexture glTextureCopy = glTexture;
            MainThreadQueue.AddToLazyQueue(() =>
            {
                /* For compressed textures:
                 * 1. Transcode the KTX texture into GPU compressed format in parallel 
                 * 2. Create staging buffer on main thread
                 * 3. Copy compressed pixels to staging buffer in parallel
                 * 4. Copy from staging buffer to texture on main thread
                 */

                /* For uncompressed textures:
                 * 1. Create staging buffer on main thread
                 * 2. Decode image and copy the pixels into staging buffer in parallel
                 * 3. Copy from staging buffer to texture on main thread
                 */

                // TODO: If the main thread is in Sleep State (for example when waiting on Parallel.For() to finish)
                //       it may end up participating as a worker in the ThreadPool.
                //       We want the main thread to only run the render loop only and not some random
                //       ThreadPool work (like loading texturs in this case), because it causes frame stutters. Fix!
                int threadPoolThreads = Math.Max(Environment.ProcessorCount / 2, 1);
                ThreadPool.SetMinThreads(threadPoolThreads, 1);
                ThreadPool.SetMaxThreads(threadPoolThreads, 1);

                bool isKtx = ktx2Texture != null;
                if (isKtx)
                {
                    Task.Run(() =>
                    {
                        //int supercompressedImageSize = (int)ktxTexture->DataSize; // Supercompressed size before transcoding
                        Ktx2.ErrorCode errorCode = ktx2Texture.Transcode(GLFormatToKtxFormat(glTextureCopy.Format), Ktx2.TranscodeFlagBits.HighQuality);
                        if (errorCode != Ktx2.ErrorCode.Success)
                        {
                            Logger.Log(Logger.LogLevel.Error, $"Failed to transcode KTX texture. {nameof(ktx2Texture.Transcode)} returned {errorCode}");
                            return;
                        }

                        MainThreadQueue.AddToLazyQueue(() =>
                        {
                            int compressedImageSize = ktx2Texture.DataSize; // Compressed size after transcoding

                            BBG.TypedBuffer<byte> stagingBuffer = new BBG.TypedBuffer<byte>();
                            stagingBuffer.ImmutableAllocate(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.MappedIncoherent, compressedImageSize);

                            Task.Run(() =>
                            {
                                Memory.Copy(ktx2Texture.Data, stagingBuffer.MappedMemory, compressedImageSize);

                                MainThreadQueue.AddToLazyQueue(() =>
                                {
                                    for (int level = 0; level < levels; level++)
                                    {
                                        ktx2Texture.GetImageOffset(level, out nint dataOffset);

                                        Vector3i size = GLTexture.GetMipmapLevelSize(ktx2Texture.BaseWidth, ktx2Texture.BaseHeight, ktx2Texture.BaseDepth, level);
                                        glTextureCopy.UploadCompressed2D(stagingBuffer, size.X, size.Y, dataOffset, level);
                                    }
                                    stagingBuffer.Dispose();
                                    ktx2Texture.Dispose();

                                    TextureLoaded?.Invoke();
                                });
                            });
                        });
                    });
                }
                else
                {
                    int imageSize = imageHeader.Width * imageHeader.Height * imageHeader.Channels;
                    BBG.TypedBuffer<byte> stagingBuffer = new BBG.TypedBuffer<byte>();
                    stagingBuffer.ImmutableAllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.MappedIncoherent, imageSize);

                    Task.Run(() =>
                    {
                        ReadOnlySpan<byte> imageData = gltfTexture.PrimaryImage.Content.Content.Span;
                        using ImageLoader.ImageResult imageResult = ImageLoader.Load(imageData, imageHeader.Channels);

                        if (imageResult.RawPixels == null)
                        {
                            Logger.Log(Logger.LogLevel.Error, $"Image \"{gltfTexture.PrimaryImage.Name}\" could not be loaded");
                            MainThreadQueue.AddToLazyQueue(() => { stagingBuffer.Dispose(); });
                            return;
                        }
                        Memory.Copy(imageResult.RawPixels, stagingBuffer.MappedMemory, imageSize);

                        MainThreadQueue.AddToLazyQueue(() =>
                        {
                            glTextureCopy.Upload2D(stagingBuffer, imageHeader.Width, imageHeader.Height, GLTexture.NumChannelsToPixelFormat(imageHeader.Channels), GLTexture.PixelType.UByte, null);
                            if (mipmapsRequired)
                            {
                                glTextureCopy.GenerateMipmap();
                            }
                            stagingBuffer.Dispose();

                            TextureLoaded?.Invoke();
                        });
                    });
                }
            });
            
            return true;
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
                    MaterialLoadData.TextureType imageType = MaterialLoadData.TextureType.BaseColor + j;
                    materialLoadData[imageType] = GetGltfTexture(gltfMaterial, imageType);
                }

                materialsLoadData[i] = materialLoadData;
                //materialsLoadData[i] = MaterialLoadData.Default;
            }

            return materialsLoadData;
        }
        private static GltfTexture GetGltfTexture(Material material, MaterialLoadData.TextureType textureType)
        {
            KnownChannel channel = textureType switch
            {
                MaterialLoadData.TextureType.BaseColor => KnownChannel.BaseColor,
                MaterialLoadData.TextureType.MetallicRoughness => KnownChannel.MetallicRoughness,
                MaterialLoadData.TextureType.Normal => KnownChannel.Normal,
                MaterialLoadData.TextureType.Emissive => KnownChannel.Emissive,
                MaterialLoadData.TextureType.Transmission => KnownChannel.Transmission,
                _ => throw new NotSupportedException($"Can not convert {nameof(textureType)} = {textureType} to {nameof(channel)}"),
            };

            MaterialChannel? materialChannel = material.FindChannel(channel.ToString());

            if (materialChannel.HasValue)
            {
                return materialChannel.Value.Texture;
            }

            return null;
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

        private static bool TryLoadInstancedNodes(Node node, out Matrix4[] nodeInstances)
        {
            MeshGpuInstancing gltfGpuInstancing = node.UseGpuInstancing();
            if (gltfGpuInstancing.Count == 0)
            {
                nodeInstances = null;
                return false;
            }

            // Gets node instances defined using EXT_mesh_gpu_instancing extension
            nodeInstances = new Matrix4[gltfGpuInstancing.Count];
            for (int i = 0; i < nodeInstances.Length; i++)
            {
                nodeInstances[i] = gltfGpuInstancing.GetLocalMatrix(i).ToOpenTK();
            }

            return true;
        }
        private static ValueTuple<GpuVertex[], Vector3[]> LoadGpuVertices(MeshPrimitive meshPrimitive)
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
                    vertices[i].Normal = Compression.CompressSR11G11B10(normal);

                    Vector3 c1 = Vector3.Cross(normal, Vector3.UnitZ);
                    Vector3 c2 = Vector3.Cross(normal, Vector3.UnitY);
                    Vector3 tangent = Vector3.Dot(c1, c1) > Vector3.Dot(c2, c2) ? c1 : c2;
                    vertices[i].Tangent = Compression.CompressSR11G11B10(tangent);
                }
            }
            else
            {
                Logger.Log(Logger.LogLevel.Error, "Mesh provides no vertex normals");
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
        private static uint[] LoadGpuIndices(MeshPrimitive meshPrimitive)
        {
            Accessor accessor = meshPrimitive.IndexAccessor;
            uint[] vertexIndices = new uint[accessor.Count];
            int componentSize = ComponentTypeToSize(accessor.Encoding);

            Span<byte> rawData = accessor.SourceBufferView.Content.AsSpan(accessor.ByteOffset, accessor.ByteLength);
            for (int i = 0; i < vertexIndices.Length; i++)
            {
                Memory.Copy(rawData[componentSize * i], ref vertexIndices[i], componentSize);
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

                Box meshletBoundingBox = Box.Empty();
                for (uint j = gpuMeshlet.VertexOffset; j < gpuMeshlet.VertexOffset + gpuMeshlet.VertexCount; j++)
                {
                    uint vertexIndex = meshMeshletsData.VertexIndices[j];
                    meshletBoundingBox.GrowToFit(meshVertexPositions[(int)vertexIndex]);
                }
                gpuMeshletInfo.Min = meshletBoundingBox.Min;
                gpuMeshletInfo.Max = meshletBoundingBox.Max;
            }

            return (gpuMeshlets, gpuMeshletsInfo);
        }

        private static MaterialParams GetMaterialParams(Material gltfMaterial)
        {
            MaterialParams materialParams = MaterialParams.Default;

            if (gltfMaterial.Alpha == SharpGLTF.Schema2.AlphaMode.OPAQUE)
            {
                materialParams.AlphaCutoff = -1.0f;
            }
            else if (gltfMaterial.Alpha == SharpGLTF.Schema2.AlphaMode.MASK)
            {
                materialParams.AlphaCutoff = gltfMaterial.AlphaCutoff;
            }
            else if (gltfMaterial.Alpha == SharpGLTF.Schema2.AlphaMode.BLEND)
            {
                // Blending only yet supported in Path Tracer
                materialParams.DoAlphaBlending = true;
            }

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

                if (materialParams.TransmissionFactor > 0.001f)
                {
                    // This is here because I only want to set IOR for transmissive objects,
                    // because for opaque objects default value of 1.5 looks bad
                    materialParams.IOR = gltfMaterial.IndexOfRefraction; // KHR_materials_ior
                }
            }

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
                    return Ktx2.TranscodeFormat.Bc1Rgb;

                case GLTexture.InternalFormat.BC4RUnorm:
                    return Ktx2.TranscodeFormat.Bc4R;

                case GLTexture.InternalFormat.BC5RgUnorm:
                    return Ktx2.TranscodeFormat.Bc5Rg;

                case GLTexture.InternalFormat.BC7RgbaUnorm:
                case GLTexture.InternalFormat.BC7RgbaSrgb:
                    return Ktx2.TranscodeFormat.Bc7Rgba;

                case GLTexture.InternalFormat.Astc4X4RgbaKHR:
                case GLTexture.InternalFormat.Astc4X4RgbaSrgbKHR:
                    return Ktx2.TranscodeFormat.Astc4X4Rgba;

                default:
                    throw new NotSupportedException($"Can not convert {nameof(internalFormat)} = {internalFormat} to {nameof(Ktx2.TranscodeFormat)}");
            }
        }
        private static int ComponentTypeToSize(EncodingType encodingType)
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
                    Meshopt.RemapVertexBuffer(vertexStreams[1].Data, vertexStreams[1].Data, (nuint)meshVertexPositions.Length, vertexStreams[1].Stride, remapTable[0]);
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
            byte[] meshletsPrimitiveIndices = new byte[maxMeshlets * MESHLET_MAX_TRIANGLE_COUNT * 3];
            nuint meshletCount = Meshopt.BuildMeshlets(
                ref meshlets[0],
                meshletsVertexIndices[0],
                meshletsPrimitiveIndices[0],
                meshIndices[0],
                (nuint)meshIndices.Length,
                meshVertexPositions[0].X,
                (nuint)meshVertexPositions.Length,
                (nuint)sizeof(Vector3),
                MESHLET_MAX_VERTEX_COUNT,
                MESHLET_MAX_TRIANGLE_COUNT,
                CONE_WEIGHT
            );

            for (int i = 0; i < meshlets.Length; i++)
            {
                ref readonly Meshopt.Meshlet meshlet = ref meshlets[i];

                // https://zeux.io/2024/04/09/meshlet-triangle-locality/
                Meshopt.OptimizeMeshlet(
                    ref meshletsVertexIndices[meshlet.VertexOffset],
                    ref meshletsPrimitiveIndices[meshlet.TriangleOffset],
                    meshlet.TriangleCount,
                    meshlet.VertexCount
                );
            }

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

        public static class CompressionUtils
        {
            public const string COMPRESSION_TOOL_NAME = "gltfpack"; // https://github.com/BoyBaykiller/meshoptimizer

            public struct CompressGltfSettings
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
            public static Task? CompressGltf(CompressGltfSettings settings)
            {
                // -v         = verbose output
                // -noq       = no mesh quanization (KHR_mesh_quantization)
                // -ac        = keep constant animation tracks even if they don't modify the node transform
                // -tc        = do KTX2 texture comression (KHR_texture_basisu)
                // -tq        = texture quality
                // -mi        = use instancing (EXT_mesh_gpu_instancing)
                // -kp        = disable mesh primitive merging (added in gltfpack fork)
                // -tj        = number of threads to use when compressing textures
                string arguments = $"-v -noq -ac -tc -tq 10 " +
                                   $"{Argument("-mi", settings.UseInstancing)} " +
                                   $"{Argument("-kp", settings.KeepMeshPrimitives)} " +
                                   $"-tj {settings.ThreadsUsed} " +
                                   $"-i {settings.InputPath} -o {settings.OutputPath}";

                ProcessStartInfo startInfo = new ProcessStartInfo()
                {
                    FileName = COMPRESSION_TOOL_NAME,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    Arguments = arguments,
                };

                try
                {
                    Logger.Log(Logger.LogLevel.Info, $"Running \"{COMPRESSION_TOOL_NAME} {arguments}\"");

                    Process? proc = Process.Start(startInfo);

                    proc.BeginErrorReadLine();
                    proc.BeginOutputReadLine();
                    proc.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                    {
                        if (e.Data == null)
                        {
                            return;
                        }

                        settings.ProcessError?.Invoke($"{COMPRESSION_TOOL_NAME}: {e.Data}");
                    };
                    proc.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                    {
                        if (e.Data == null)
                        {
                            return;
                        }

                        settings.ProcessOutput?.Invoke($"{COMPRESSION_TOOL_NAME}: {e.Data}");
                    };

                    return proc.WaitForExitAsync();
                }
                catch (Exception)
                {
                    Logger.Log(Logger.LogLevel.Error, $"Failed to create process. Be sure to provide a {COMPRESSION_TOOL_NAME} binary (https://github.com/BoyBaykiller/meshoptimizer) in working dir or PATH");
                    return null;
                }

                static string Argument(string argument, bool yes)
                {
                    if (yes)
                    {
                        return argument;
                    }

                    return string.Empty;
                }
            }

            public static bool CompressionToolAvailable()
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

                    string[] results = Directory.GetFiles(envPath, $"{COMPRESSION_TOOL_NAME}.*");
                    if (results.Length > 0)
                    {
                        return true;
                    }
                }

                static bool TryGetEnvironmentVariable(string envVar, out string[] strings)
                {
                    string? data = Environment.GetEnvironmentVariable(envVar);
                    strings = data?.Split(';');

                    return data != null;
                }

                return false;
            }

            public static bool IsCompressedGltf(ModelRoot gltf)
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
        }

        private static class FallbackTextures
        {
            private static GLTexture pureWhiteTexture;
            private static GLTexture.BindlessHandle pureWhiteTextureHandle;

            private static GLTexture purpleBlackTexture;
            private static GLTexture.BindlessHandle purpleBlackTextureHandle;

            public static GLTexture.BindlessHandle PureWhite()
            {
                if (pureWhiteTexture == null)
                {
                    pureWhiteTexture = new GLTexture(GLTexture.Type.Texture2D);
                    pureWhiteTexture.ImmutableAllocate(1, 1, 1, GLTexture.InternalFormat.R16G16B16A16Float);
                    pureWhiteTexture.Clear(GLTexture.PixelFormat.RGBA, GLTexture.PixelType.Float, new Vector4(1.0f));
                    pureWhiteTextureHandle = pureWhiteTexture.GetTextureHandleARB(new GLSampler(new GLSampler.SamplerState()));
                }
                return pureWhiteTextureHandle;
            }

            public static GLTexture.BindlessHandle PurpleBlack()
            {
                if (purpleBlackTexture == null)
                {
                    purpleBlackTexture = new GLTexture(GLTexture.Type.Texture2D);
                    purpleBlackTexture.ImmutableAllocate(2, 2, 1, GLTexture.InternalFormat.R16G16B16A16Float);
                    purpleBlackTexture.Upload2D(2, 2, GLTexture.PixelFormat.RGBA, GLTexture.PixelType.Float, new Vector4[]
                    {
                        // Source: https://en.wikipedia.org/wiki/File:Minecraft_missing_texture_block.svg
                        new Vector4(251.0f / 255.0f, 62.0f / 255.0f, 249.0f / 255.0f, 1.0f), // Purple
                        new Vector4(0.0f, 0.0f, 0.0f, 1.0f), // Black
                        new Vector4(0.0f, 0.0f, 0.0f, 1.0f), // Black
                        new Vector4(251.0f / 255.0f, 62.0f / 255.0f, 249.0f / 255.0f, 1.0f), // Purple
                    }[0]);

                    purpleBlackTextureHandle = purpleBlackTexture.GetTextureHandleARB(new GLSampler(new GLSampler.SamplerState()
                    {
                        WrapModeS = GLSampler.WrapMode.Repeat,
                        WrapModeT = GLSampler.WrapMode.Repeat
                    }));
                }

                return purpleBlackTextureHandle;
            }
        }
    }
}
