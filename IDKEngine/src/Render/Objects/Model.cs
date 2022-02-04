using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using Assimp;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace IDKEngine.Render.Objects
{
    class Model
    {
        public const int GLSL_INSTANCE_COUNT = 1;
        public const int GLSL_TEXTURE_SIZE = sizeof(long) * 2;
        private static readonly AssimpContext assimpContext = new AssimpContext();

        private static readonly TextureType[] perMaterialTextures = new TextureType[]
        {
            TextureType.Diffuse /* Albedo */,
            TextureType.Normals,
            TextureType.Ambient /* Metallic */,
            TextureType.Shininess /* Roughness */,
            TextureType.Specular,
        };

        public struct GLSLDrawCommand
        {
            public int Count;
            public int InstanceCount;
            public int FirstIndex;
            public int BaseVertex;
            public int BaseInstance;
        }
        public unsafe struct GLSLMaterial
        {
            public readonly long Albedo;
            private readonly long _pad0;

            public readonly long Normal;
            private readonly long _pad1;

            public readonly long Metallic;
            private readonly long _pad2;

            public readonly long Roughness;
            private readonly long _pad3;

            public readonly long Specular;
            private readonly long _pad4;
        }
        public struct GLSLMesh
        {
            public Matrix4 Model;
            public int MaterialIndex;
            public int BaseNode;
            private readonly float _pad0;
            private readonly float _pad1;
        }
        public struct GLSLVertex
        {
            public Vector3 Position;
            private readonly float _pad0;
            public Vector2 TexCoord;
            private readonly Vector2 _pad1;
            public Vector3 Normal;
            private readonly float _pad2;
            public Vector3 Tangent;
            private readonly float _pad3;
            public Vector3 BiTangent;
            private readonly float _pad4;
        }

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
                                                   PostProcessSteps.ImproveCacheLocality | PostProcessSteps.JoinIdenticalVertices |  PostProcessSteps.RemoveRedundantMaterials | // PostProcessSteps.OptimizeMeshes | PostProcessSteps.OptimizeGraph | 
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

            while (!vertecisLoadResult.IsCompleted && !texturesLoadResult.IsCompleted) ;

            List<uint> indices = new List<uint>();
            for (int i = 0; i < scene.MeshCount; i++)
            {
                DrawCommands[i].FirstIndex = indices.Count;
                    
                uint[] thisIndices = scene.Meshes[i].GetUnsignedIndices();
                indices.AddRange(thisIndices);
                    
                DrawCommands[i].Count = thisIndices.Length;
            }
            Indices = indices.ToArray();

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
                        texture.ImmutableAllocate(img.Width, img.Height, 1, (SizedInternalFormat)format, Texture.GetMaxMipMaplevel(img.Width, img.Height, 1));
                        fixed (void* ptr = img.GetPixelRowSpan(0))
                        {
                            texture.SubTexture2D(img.Width, img.Height, PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ptr);
                        }
                        texture.GenerateMipmap();
                        texture.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
                        texture.SetAnisotropy(4.0f);
                        img.Dispose();
                    }
                    else
                    {
                        // Create dummy texture
                        texture.ImmutableAllocate(1, 1, 1, (SizedInternalFormat)format);
                    }
                    long textureHandle = texture.MakeHandleResident();

                    /// Yes I preferre this pointer trickery over a long switch statement
                    fixed (void* ptr = &Materials[i].Albedo)
                    {
                        *(long*)((byte*)ptr + GLSL_TEXTURE_SIZE * j) = textureHandle;
                    }
                }
            }
        }
    }
}
