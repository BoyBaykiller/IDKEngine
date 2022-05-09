using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using Assimp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace IDKEngine.Render.Objects
{
    class Model
    {
        public const int GLSL_TEXTURE_SIZE = sizeof(long) * 2;
        private static readonly AssimpContext assimpContext = new AssimpContext();

        private static readonly TextureType[] perMaterialTextures = new TextureType[]
        {
            TextureType.Diffuse /* Albedo */,
            TextureType.Normals,
            TextureType.Shininess /* Roughness */,
            TextureType.Specular,
        };

        public readonly GLSLDrawCommand[] DrawCommands;
        public readonly GLSLMesh[] Meshes;
        public readonly GLSLMaterial[] Materials;
        public readonly GLSLVertex[] Vertices;
        public readonly uint[] Indices;

        public readonly Matrix4[][] Models;

        private readonly Scene scene;
        public unsafe Model(string path)
        {
            string dirPath = Path.GetDirectoryName(path);
            scene = assimpContext.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.CalculateTangentSpace | PostProcessSteps.JoinIdenticalVertices |
                                                   PostProcessSteps.ImproveCacheLocality | PostProcessSteps.JoinIdenticalVertices | PostProcessSteps.RemoveRedundantMaterials | // PostProcessSteps.OptimizeMeshes | PostProcessSteps.OptimizeGraph | 
                                                   PostProcessSteps.FlipUVs);
            Debug.Assert(scene != null);

            DrawCommands = new GLSLDrawCommand[scene.MeshCount];
            Meshes = new GLSLMesh[scene.MeshCount];
            Materials = new GLSLMaterial[scene.MaterialCount];
            Vertices = new GLSLVertex[scene.Meshes.Sum(mesh => mesh.VertexCount)];
            Models = new Matrix4[scene.MeshCount][];
            Image<Rgba32>[] images = new Image<Rgba32>[scene.MaterialCount * perMaterialTextures.Length];

            ParallelLoopResult vertecisLoadResult = Parallel.For(0, scene.MeshCount, i =>
            {
                Mesh mesh = scene.Meshes[i];

                int baseVertex = 0;
                for (int j = 0; j < i; j++)
                    baseVertex += scene.Meshes[j].VertexCount;

                DrawCommands[i].BaseVertex = baseVertex;

                for (int j = 0; j < mesh.VertexCount; j++)
                {
                    Vertices[baseVertex + j].Position.X = mesh.Vertices[j].X;
                    Vertices[baseVertex + j].Position.Y = mesh.Vertices[j].Y;
                    Vertices[baseVertex + j].Position.Z = mesh.Vertices[j].Z;

                    Vertices[baseVertex + j].TexCoord.X = mesh.TextureCoordinateChannels[0][j].X;
                    Vertices[baseVertex + j].TexCoord.Y = mesh.TextureCoordinateChannels[0][j].Y;
                      
                    Vertices[baseVertex + j].Normal.X = mesh.Normals[j].X;
                    Vertices[baseVertex + j].Normal.Y = mesh.Normals[j].Y;
                    Vertices[baseVertex + j].Normal.Z = mesh.Normals[j].Z;

                    Vertices[baseVertex + j].Tangent.X = mesh.Tangents[j].X;
                    Vertices[baseVertex + j].Tangent.Y = mesh.Tangents[j].Y;
                    Vertices[baseVertex + j].Tangent.Z = mesh.Tangents[j].Z;

                    Vertices[baseVertex + j].BiTangent.X = mesh.BiTangents[j].X;
                    Vertices[baseVertex + j].BiTangent.Y = mesh.BiTangents[j].Y;
                    Vertices[baseVertex + j].BiTangent.Z = mesh.BiTangents[j].Z;
                }

                Models[i] = new Matrix4[1] { Matrix4.Identity };

                Meshes[i].InstanceCount = Models[i].Length;
                Meshes[i].MaterialIndex = mesh.MaterialIndex;
                Meshes[i].MatrixStart = i;
                Meshes[i].NormalMapStrength = scene.Materials[mesh.MaterialIndex].HasTextureNormal ? 1.0f : 0.0f;
                Meshes[i].SpecularChance = 0.5f;
                Meshes[i].Roughness = 0.5f;

                // Drawcommand instance count may differ depending on culling. Mesh instance count doesn't
                DrawCommands[i].InstanceCount = Meshes[i].InstanceCount;
                DrawCommands[i].BaseInstance = 0;
            });

            ParallelLoopResult texturesLoadResult = Parallel.For(0, scene.MaterialCount * perMaterialTextures.Length, i =>
            {
                int materialIndex = i / perMaterialTextures.Length;
                int textureIndex = i % perMaterialTextures.Length;
                
                Material material = scene.Materials[materialIndex];
                if (material.GetMaterialTexture(perMaterialTextures[textureIndex], 0, out TextureSlot textureSlot))
                    images[i] = Image.Load<Rgba32>(Path.Combine(dirPath, textureSlot.FilePath));
            });

            List<uint> indices = new List<uint>();
            for (int i = 0; i < scene.MeshCount; i++)
            {
                DrawCommands[i].FirstIndex = indices.Count;

                uint[] thisIndices = scene.Meshes[i].GetUnsignedIndices();
                indices.AddRange(thisIndices);

                DrawCommands[i].Count = thisIndices.Length;
            }
            Indices = indices.ToArray();

            while (!vertecisLoadResult.IsCompleted && !texturesLoadResult.IsCompleted) ;

            PreTransformMeshes(scene.RootNode, AssimpToOpenTKMat4(scene.RootNode.Transform));
            void PreTransformMeshes(Node node, Matrix4 model)
            {
                Matrix4 parent = model;
                for (int i = 0; i < node.ChildCount; i++)
                {
                    Matrix4 child = parent * AssimpToOpenTKMat4(node.Children[i].Transform);
                    for (int j = 0; j < node.Children[i].MeshCount; j++)
                    {
                        Models[node.Children[i].MeshIndices[j]][0] *= child;
                    }
                    PreTransformMeshes(node.Children[i], child);
                }
            }

            for (int i = 0; i < Materials.Length; i++)
            {
                for (int j = 0; j < perMaterialTextures.Length; j++)
                {
                    Texture texture = new Texture(TextureTarget2d.Texture2D);
                    SizedInternalFormat format;
                    switch (perMaterialTextures[j])
                    {
                        case TextureType.Diffuse:
                            format = SizedInternalFormat.Srgb8Alpha8;
                            break;

                        case TextureType.Shininess or TextureType.Specular or TextureType.Metalness or TextureType.Ambient or TextureType.Roughness:
                            format = SizedInternalFormat.R8;
                            break;

                        default:
                            format = SizedInternalFormat.Rgba8;
                            break;
                    }

                    Image<Rgba32> img = images[perMaterialTextures.Length * i + j];
                    if (img != null)
                    {
                        texture.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
                        texture.SetWrapMode(OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat, OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat);
                        texture.ImmutableAllocate(img.Width, img.Height, 1, format, Texture.GetMaxMipmapLevel(img.Width, img.Height, 1));
                        fixed (void* ptr = img.GetPixelRowSpan(0))
                        {
                            texture.SubTexture2D(img.Width, img.Height, PixelFormat.Rgba, PixelType.UnsignedByte, (System.IntPtr)ptr);
                        }
                        texture.GenerateMipmap();
                        texture.SetAnisotropy(4.0f);

                        img.Dispose();
                    }
                    else
                    {
                        // Create dummy texture
                        texture.ImmutableAllocate(1, 1, 1, format);
                    }
                    long textureHandle = texture.MakeHandleResidentARB();

                    /// Yes I prefer this pointer trickery over a long switch statement
                    fixed (void* ptr = &Materials[i].Albedo)
                    {
                        *(long*)((byte*)ptr + GLSL_TEXTURE_SIZE * j) = textureHandle;
                    }
                }
            }
        }

        private static unsafe Matrix4 AssimpToOpenTKMat4(Matrix4x4 matrix)
        {
            Matrix4 result = *(Matrix4*)&matrix;
            result.Transpose();
            return result;
        }
    }
}
