using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ModelSystem
    {
        public const int GLSL_MAX_UBO_MATERIAL_COUNT = 256; // used in shader and client code - keep in sync!

        public GLSLDrawCommand[] DrawCommands;
        private readonly BufferObject drawCommandBuffer;

        public GLSLMesh[] Meshes;
        private readonly BufferObject meshBuffer;

        public GLSLMaterial[] Materials;
        private readonly BufferObject materialBuffer;

        public GLSLVertex[] Vertices;
        private readonly BufferObject vertexBuffer;

        public uint[] Indices;
        private readonly BufferObject elementBuffer;

        public Matrix4[][] ModelMatrices;
        private readonly BufferObject modelMatricesBuffer;

        public readonly VAO VAO;
        public unsafe ModelSystem()
        {
            DrawCommands = new GLSLDrawCommand[0];
            drawCommandBuffer = new BufferObject();
            drawCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0);

            Meshes = new GLSLMesh[0];
            meshBuffer = new BufferObject();
            meshBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2);

            Materials = new GLSLMaterial[0];
            materialBuffer = new BufferObject();
            materialBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 1);

            Vertices = new GLSLVertex[0];
            vertexBuffer = new BufferObject();

            Indices = new uint[0];
            elementBuffer = new BufferObject();

            ModelMatrices = new Matrix4[0][];
            modelMatricesBuffer = new BufferObject();
            modelMatricesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4);

            VAO = new VAO();
            VAO.SetElementBuffer(elementBuffer);
            VAO.AddSourceBuffer(vertexBuffer, 0, sizeof(GLSLVertex));
            VAO.SetAttribFormat(0, 0, 3, VertexAttribType.Float, sizeof(float) * 0); // Position
            VAO.SetAttribFormat(0, 1, 1, VertexAttribType.Float, sizeof(float) * 3); // TexCoordU
            VAO.SetAttribFormat(0, 2, 3, VertexAttribType.Float, sizeof(float) * 4); // Normal
            VAO.SetAttribFormat(0, 3, 1, VertexAttribType.Float, sizeof(float) * 7); // TexCoordV
            VAO.SetAttribFormat(0, 4, 3, VertexAttribType.Float, sizeof(float) * 8); // Tangent
        }

        public unsafe void Add(Model[] models)
        {
            int oldDrawCommandsLength = DrawCommands.Length;
            int oldMeshesLength = Meshes.Length;
            int oldMaterialsLength = Materials.Length;
            int oldVerticesLength = Vertices.Length;
            int oldIndicesLength = Indices.Length;
            int oldModelsLength = ModelMatrices.Length;

            int loadedDrawCommands = 0;
            int loadedMeshes = 0;
            int loadedMaterials = 0;
            int loadedVertices = 0;
            int loadedIndices = 0;
            int loadedModels = 0;

            Array.Resize(ref DrawCommands, DrawCommands.Length + models.Sum(m => m.DrawCommands.Length));
            Array.Resize(ref Meshes, Meshes.Length + models.Sum(m => m.Meshes.Length));
            Array.Resize(ref Materials, Materials.Length + models.Sum(m => m.Materials.Length));
            Array.Resize(ref Vertices, Vertices.Length + models.Sum(m => m.Vertices.Length));
            Array.Resize(ref Indices, Indices.Length + models.Sum(m => m.Indices.Length));
            Array.Resize(ref ModelMatrices, ModelMatrices.Length + models.Sum(m => m.Models.Length));

            Debug.Assert(Materials.Length <= GLSL_MAX_UBO_MATERIAL_COUNT);

            for (int i = 0; i < models.Length; i++)
            {
                Model model = models[i];
                
                Array.Copy(model.DrawCommands, 0, DrawCommands, oldDrawCommandsLength + loadedDrawCommands, model.DrawCommands.Length);
                Array.Copy(model.Meshes, 0, Meshes, oldMeshesLength + loadedMeshes, model.Meshes.Length);
                Array.Copy(model.Materials, 0, Materials, oldMaterialsLength + loadedMaterials, model.Materials.Length);
                Array.Copy(model.Vertices, 0, Vertices, oldVerticesLength + loadedVertices, model.Vertices.Length);
                Array.Copy(model.Indices, 0, Indices, oldIndicesLength + loadedIndices, model.Indices.Length);
                Array.Copy(model.Models, 0, ModelMatrices, oldModelsLength + loadedModels, model.Models.Length);

                for (int j = oldDrawCommandsLength + loadedDrawCommands; j < oldDrawCommandsLength + loadedDrawCommands + model.DrawCommands.Length; j++)
                {
                    // TODO: Fix calculation of base instance to account for more than 1 instance per model
                    DrawCommands[j].BaseInstance += oldModelsLength + loadedModels;
                    DrawCommands[j].BaseVertex += oldVerticesLength + loadedVertices;
                    DrawCommands[j].FirstIndex += oldIndicesLength + loadedIndices;
                }

                for (int j = oldMeshesLength + loadedMeshes; j < oldMeshesLength + loadedMeshes + model.Meshes.Length; j++)
                {
                    Meshes[j].MaterialIndex += oldMaterialsLength + loadedMaterials;
                }

                loadedModels += model.Models.Length;
                loadedDrawCommands += model.DrawCommands.Length;
                loadedMeshes += model.Meshes.Length;
                loadedMaterials += model.Materials.Length;
                loadedVertices += model.Vertices.Length;
                loadedIndices += model.Indices.Length;
            }

            drawCommandBuffer.MutableAllocate(DrawCommands.Length * sizeof(GLSLDrawCommand), DrawCommands);
            meshBuffer.MutableAllocate(Meshes.Length * sizeof(GLSLMesh), Meshes);
            materialBuffer.MutableAllocate(Materials.Length * sizeof(GLSLMaterial), Materials);
            vertexBuffer.MutableAllocate(Vertices.Length * sizeof(GLSLVertex), Vertices);
            elementBuffer.MutableAllocate(Indices.Length * sizeof(uint), Indices);
            modelMatricesBuffer.MutableAllocate(ModelMatrices.Sum(arr => arr.Length) * sizeof(Matrix4), (IntPtr)0);

            UpdateModelMatricesBuffer(0, ModelMatrices.Length);
        }

        public void Draw()
        {
            VAO.Bind();
            drawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer);

            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, (IntPtr)0, Meshes.Length, 0);
        }

        private static readonly ShaderProgram cullingProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/compute.glsl")));
        public void ViewCull(ref Matrix4 projView)
        {
            cullingProgram.Use();
            cullingProgram.Upload(0, ref projView);

            GL.DispatchCompute((Meshes.Length + 64 - 1) / 64, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
        }

        public delegate void FuncUploadMesh(ref GLSLMesh glslMesh);
        /// <summary>
        /// Synchronizes buffer with local <see cref="Meshes"/> and conditionally applies function over all elements
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="func"></param>
        public unsafe void UpdateMeshBuffer(int start, int end, FuncUploadMesh func = null)
        {
            Debug.Assert(start >= 0 && end <= Meshes.Length);

            if (func != null)
            {
                for (int i = start; i < end; i++)
                    func(ref Meshes[i]);
            }

            fixed (void* ptr = &Meshes[start])
            {
                meshBuffer.SubData(start * sizeof(GLSLMesh), (end - start) * sizeof(GLSLMesh), (IntPtr)ptr);
            }
        }


        public delegate void FuncUploadDrawCommand(ref GLSLDrawCommand drawCommand);
        /// <summary>
        /// Synchronizes buffer with local <see cref="DrawCommands"/> and conditionally applies function over all elements
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="func"></param>
        public unsafe void UpdateDrawCommandBuffer(int start, int end, FuncUploadDrawCommand func = null)
        {
            Debug.Assert(start >= 0 && end <= DrawCommands.Length);

            if (func != null)
            {
                for (int i = start; i < end; i++)
                    func(ref DrawCommands[i]);
            }

            fixed (void* ptr = &DrawCommands[start])
            {
                drawCommandBuffer.SubData(start * sizeof(GLSLDrawCommand), (end - start) * sizeof(GLSLDrawCommand), (IntPtr)ptr);
            }
        }


        /// <summary>
        /// Synchronizes buffer with local <see cref="ModelMatrices"/>
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="func"></param>
        public unsafe void UpdateModelMatricesBuffer(int start, int end)
        {
            Debug.Assert(start >= 0 && end <= ModelMatrices.Length);

            int updatedModelCounter = 0;
            for (int i = 0; i < ModelMatrices.Length; i++)
            {
                modelMatricesBuffer.SubData(updatedModelCounter * sizeof(Matrix4), ModelMatrices[i].Length * sizeof(Matrix4), ModelMatrices[i]);
                updatedModelCounter += ModelMatrices[i].Length;
            }
        }

        public int GetMeshVertexCount(int meshIndex)
        {
            Debug.Assert(meshIndex < Meshes.Length);

            int nextMeshBaseVertex = (meshIndex < Meshes.Length - 1) ? DrawCommands[meshIndex + 1].BaseVertex : Vertices.Length;
            return nextMeshBaseVertex - DrawCommands[meshIndex].BaseVertex;
        }
    }
}
