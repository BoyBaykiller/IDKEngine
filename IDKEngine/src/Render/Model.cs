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

                Meshes[i].MaterialIndex = mesh.MaterialIndex;
                Meshes[i].Model = Matrix4.Identity;
                Meshes[i].NormalMapStrength = scene.Materials[mesh.MaterialIndex].HasTextureNormal ? 1.0f : 0.0f;
                Meshes[i].SpecularChance = 0.5f;
                Meshes[i].Roughness = 0.5f;

                DrawCommands[i].InstanceCount = 1;
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

            PreTransformMeshes(scene.RootNode);
            void PreTransformMeshes(Node node)
            {
                for (int i = 0; i < node.ChildCount; i++)
                {
                    for (int j = 0; j < node.Children[i].MeshCount; j++)
                    {
                        Meshes[node.Children[i].MeshIndices[j]].Model *= AssimpToOpenTKMat4(node.Children[i].Transform);
                    }
                    PreTransformMeshes(node.Children[i]);
                }
            }

            for (int i = 0; i < Materials.Length; i++)
            {
                for (int j = 0; j < perMaterialTextures.Length; j++)
                {
                    Texture texture = new Texture(TextureTarget2d.Texture2D);
                    PixelInternalFormat format;
                    switch (perMaterialTextures[j])
                    {
                        case TextureType.Diffuse:
                            format = PixelInternalFormat.Srgb8Alpha8;
                            break;

                        case TextureType.Shininess or TextureType.Specular or TextureType.Metalness or TextureType.Ambient or TextureType.Roughness:
                            format = PixelInternalFormat.R8;
                            break;

                        default:
                            format = PixelInternalFormat.Rgba8;
                            break;
                    }

                    Image<Rgba32> img = images[perMaterialTextures.Length * i + j];
                    if (img != null)
                    {
                        texture.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
                        texture.SetWrapMode(OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat, OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat);
                        texture.ImmutableAllocate(img.Width, img.Height, 1, (SizedInternalFormat)format, Texture.GetMaxMipMaplevel(img.Width, img.Height, 1));
                        texture.SubTexture2D(img.Width, img.Height, PixelFormat.Rgba, PixelType.UnsignedByte, img.GetPixelRowSpan(0).ToPtr());
                        texture.GenerateMipmap();
                        if (Helper.IsCoreExtensionAvailable("GL_ARB_texture_filter_anisotropic", 4.6) || Helper.IsExtensionsAvailable("GL_ARB_texture_filter_anisotropic") || Helper.IsExtensionsAvailable("GL_EXT_texture_filter_anisotropic"))
                            texture.SetAnisotropy(4.0f);
                        
                        img.Dispose();
                    }
                    else
                    {
                        // Create dummy texture
                        texture.ImmutableAllocate(1, 1, 1, (SizedInternalFormat)format);
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
