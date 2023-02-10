using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using Assimp;
using StbImageSharp;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Model
    {
        private static readonly AssimpContext assimpContext = new AssimpContext();

        // If made changes also adjust the GLSLMaterial struct
        private static readonly TextureType[] perMaterialTextures = new TextureType[]
        {
            TextureType.Diffuse, // Albedo
            TextureType.Normals,
            TextureType.Shininess, // Roughness 
            TextureType.Specular,
            TextureType.Emissive,
        };

        public readonly GLSLDrawCommand[] DrawCommands;
        public readonly GLSLMesh[] Meshes;
        public readonly GLSLMeshInstance[] MeshInstances;
        public readonly GLSLMaterial[] Materials;
        public readonly GLSLDrawVertex[] Vertices;
        public readonly uint[] Indices;


        public unsafe Model(string path)
        {
            string dirPath = Path.GetDirectoryName(path);

            Scene scene = assimpContext.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices | PostProcessSteps.GenerateNormals |
                                                   PostProcessSteps.RemoveRedundantMaterials | PostProcessSteps.PreTransformVertices |
                                                   PostProcessSteps.FlipUVs);
            
            Debug.Assert(scene != null);

            DrawCommands = new GLSLDrawCommand[scene.MeshCount];
            Meshes = new GLSLMesh[scene.MeshCount];
            Materials = new GLSLMaterial[scene.MaterialCount];
            Vertices = new GLSLDrawVertex[scene.Meshes.Sum(mesh => mesh.VertexCount)];
            MeshInstances = new GLSLMeshInstance[scene.MeshCount];
            ImageResult[] images = new ImageResult[scene.MaterialCount * perMaterialTextures.Length];
            Thread verticesLoadResult = Helper.InParallel(0, scene.MeshCount, i =>
            {
                Mesh mesh = scene.Meshes[i];

                int baseVertex = 0;
                for (int j = 0; j < i; j++)
                {
                    baseVertex += scene.Meshes[j].VertexCount;
                }
                DrawCommands[i].BaseVertex = baseVertex;

                for (int j = 0; j < mesh.VertexCount; j++)
                {
                    Vertices[baseVertex + j].Position.X = mesh.Vertices[j].X;
                    Vertices[baseVertex + j].Position.Y = mesh.Vertices[j].Y;
                    Vertices[baseVertex + j].Position.Z = mesh.Vertices[j].Z;

                    if (mesh.TextureCoordinateChannels[0].Count > 0)
                    {
                        Vertices[baseVertex + j].TexCoord.X = mesh.TextureCoordinateChannels[0][j].X;
                        Vertices[baseVertex + j].TexCoord.Y = mesh.TextureCoordinateChannels[0][j].Y;
                    }

                    Vector3 normal = new Vector3(mesh.Normals[j].X, mesh.Normals[j].Y, mesh.Normals[j].Z);
                    Vertices[baseVertex + j].Normal = Helper.CompressSNorm32Fast(normal);
                    
                    Vector3 c1 = Vector3.Cross(normal, Vector3.UnitZ);
                    Vector3 c2 = Vector3.Cross(normal, Vector3.UnitY);
                    Vector3 tangent = Vector3.Dot(c1, c1) > Vector3.Dot(c2, c2) ? c1 : c2;
                    Vertices[baseVertex + j].Tangent = Helper.CompressSNorm32Fast(tangent);
                }

                MeshInstances[i].ModelMatrix = Matrix4.Identity;

                Meshes[i].InstanceCount = 1;
                Meshes[i].MaterialIndex = mesh.MaterialIndex;
                Meshes[i].NormalMapStrength = scene.Materials[mesh.MaterialIndex].HasTextureNormal ? 1.0f : 0.0f;
                Meshes[i].IOR = 1.0f;

                // Drawcommand instance count may differ depending on culling. Mesh instance count doesn't
                DrawCommands[i].InstanceCount = Meshes[i].InstanceCount;
                DrawCommands[i].BaseInstance = i;
            });

            Thread texturesLoadResult = Helper.InParallel(0, scene.MaterialCount * perMaterialTextures.Length, i =>
            {
                int materialIndex = i / perMaterialTextures.Length;
                int textureIndex = i % perMaterialTextures.Length;
                
                Material material = scene.Materials[materialIndex];
                if (material.GetMaterialTexture(perMaterialTextures[textureIndex], 0, out TextureSlot textureSlot))
                {
                    string path = Path.Combine(dirPath, textureSlot.FilePath);
                    if (File.Exists(path))
                    {
                        using FileStream stream = File.OpenRead(path);
                        images[i] = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                    }
                    else
                    {
                        Console.WriteLine($"{path} is not found");
                    }
                }
            });

            List<uint> indices = new List<uint>(scene.Meshes.Sum(m => m.VertexCount));
            verticesLoadResult.Join();
            for (int i = 0; i < scene.MeshCount; i++)
            {
                DrawCommands[i].FirstIndex = indices.Count;

                uint[] thisIndices = scene.Meshes[i].GetUnsignedIndices();
                indices.AddRange(thisIndices);

                DrawCommands[i].Count = thisIndices.Length;
            }
            Indices = indices.ToArray();
            
            texturesLoadResult.Join();
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

                        case TextureType.Normals:
                            format = SizedInternalFormat.Rgb8;
                            break;

                        case TextureType.Shininess: // Roughness
                            format = SizedInternalFormat.R8;
                            break;

                        case TextureType.Specular:
                            format = SizedInternalFormat.R8;
                            break;

                        case TextureType.Emissive:
                            format = SizedInternalFormat.Rgb8;
                            break;

                        default:
                            const SizedInternalFormat def = SizedInternalFormat.Rgba8;
                            Console.WriteLine($"Unhandled texture type: {perMaterialTextures[j]}. Default to {def}");
                            format = def;
                            break;
                    }

                    ImageResult img = images[perMaterialTextures.Length * i + j];
                    if (img != null)
                    {
                        texture.SetFilter(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Linear);
                        texture.SetWrapMode(OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat, OpenTK.Graphics.OpenGL4.TextureWrapMode.Repeat);
                        texture.ImmutableAllocate(img.Width, img.Height, 1, format, Math.Max(Texture.GetMaxMipmapLevel(img.Width, img.Height, 1), 1));
                        texture.SubTexture2D(img.Width, img.Height, PixelFormat.Rgba, PixelType.UnsignedByte, img.Data);
                        texture.GenerateMipmap();
                        texture.SetAnisotropy(4.0f);
                    }
                    else
                    {
                        // Create dummy texture
                        texture.ImmutableAllocate(1, 1, 1, format);

                        if (perMaterialTextures[j] == TextureType.Diffuse)
                        {
                            texture.Clear(PixelFormat.Rgba, PixelType.Float, new Vector4(1.0f));
                        }
                    }
                    ulong textureHandle = texture.MakeTextureHandleARB();

                    /// Yes I prefer this pointer trickery over a long switch statement
                    fixed (ulong* ptr = &Materials[i].AlbedoAlpha)
                    {
                        *(ptr + j) = textureHandle;
                    }
                }
            }
        }
    }
}
