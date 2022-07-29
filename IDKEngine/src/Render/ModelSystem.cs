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
        public GLSLDrawCommand[] DrawCommands;
        private readonly BufferObject drawCommandBuffer;

        public GLSLMesh[] Meshes;
        private readonly BufferObject meshBuffer;

        public GLSLMaterial[] Materials;
        private readonly BufferObject materialBuffer;

        public GLSLDrawVertex[] Vertices;
        private readonly BufferObject vertexBuffer;

        public uint[] Indices;
        private readonly BufferObject elementBuffer;

        public Matrix4[][] ModelMatrices;
        private readonly BufferObject modelMatricesBuffer;

        private readonly VAO vao;
        public unsafe ModelSystem()
        {
            DrawCommands = Array.Empty<GLSLDrawCommand>();
            drawCommandBuffer = new BufferObject();
            drawCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0);

            Meshes = Array.Empty<GLSLMesh>();
            meshBuffer = new BufferObject();
            meshBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2);

            Materials = Array.Empty<GLSLMaterial>();
            materialBuffer = new BufferObject();
            materialBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5);

            Vertices = Array.Empty<GLSLDrawVertex>();
            vertexBuffer = new BufferObject();

            Indices = Array.Empty<uint>();
            elementBuffer = new BufferObject();

            ModelMatrices = Array.Empty<Matrix4[]>();
            modelMatricesBuffer = new BufferObject();
            modelMatricesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4);

            vao = new VAO();
            vao.SetElementBuffer(elementBuffer);
            vao.AddSourceBuffer(vertexBuffer, 0, sizeof(GLSLDrawVertex));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, sizeof(float) * 0); // Position
            vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, sizeof(float) * 4); // TexCoord
            vao.SetAttribFormatI(0, 2, 1, VertexAttribType.UnsignedInt, sizeof(float) * 6); // Tangent
            vao.SetAttribFormatI(0, 3, 1, VertexAttribType.UnsignedInt, sizeof(float) * 7); // Normal
        }

        public unsafe void Add(Model[] models)
        {
            if (models.Length == 0)
                return;

            for (int i = 0; i < models.Length; i++)
            {
                // Don't modify order
                LoadDrawCommands(models[i].DrawCommands);
                LoadVertices(models[i].Vertices);

                // Don't modify order
                LoadMeshes(models[i].Meshes);
                LoadMaterials(models[i].Materials);

                LoadIndices(models[i].Indices);
                LoadModelMatrices(models[i].ModelMatrices);
            }

            drawCommandBuffer.MutableAllocate(DrawCommands.Length * sizeof(GLSLDrawCommand), DrawCommands);
            meshBuffer.MutableAllocate(Meshes.Length * sizeof(GLSLMesh), Meshes);
            materialBuffer.MutableAllocate(Materials.Length * sizeof(GLSLMaterial), Materials);
            vertexBuffer.MutableAllocate(Vertices.Length * sizeof(GLSLDrawVertex), Vertices);
            elementBuffer.MutableAllocate(Indices.Length * sizeof(uint), Indices);
            modelMatricesBuffer.MutableAllocate(ModelMatrices.Sum(arr => arr.Length) * sizeof(Matrix4), (IntPtr)0);

            UpdateModelMatricesBuffer(0, ModelMatrices.Length);
        }

        public void Draw()
        {
            vao.Bind();
            drawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer);

            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, (IntPtr)0, Meshes.Length, 0);
        }

        private static readonly ShaderProgram frustumCullingProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/Frustum/compute.glsl")));
        public void FrustumCull(ref Matrix4 projView)
        {
            frustumCullingProgram.Use();
            frustumCullingProgram.Upload(0, ref projView);

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

            for (int i = start; i < end; i++)
            {
                modelMatricesBuffer.SubData(DrawCommands[i].BaseInstance * sizeof(Matrix4), ModelMatrices[i].Length * sizeof(Matrix4), ModelMatrices[i]);
            }
        }

        public int GetMeshVertexCount(int meshIndex)
        {
            return ((meshIndex + 1 > DrawCommands.Length - 1) ? Vertices.Length : DrawCommands[meshIndex + 1].BaseVertex) - DrawCommands[meshIndex].BaseVertex;
        }

        private void LoadDrawCommands(GLSLDrawCommand[] drawCommands)
        {
            int prevCmdLength = DrawCommands.Length;
            int prevIndicesLength = DrawCommands.Length == 0 ? 0 : DrawCommands[prevCmdLength - 1].FirstIndex + DrawCommands[prevCmdLength - 1].Count;
            int prevBaseVertex = DrawCommands.Length == 0 ? 0 : DrawCommands[prevCmdLength - 1].BaseVertex + GetMeshVertexCount(prevCmdLength - 1);

            Array.Resize(ref DrawCommands, prevCmdLength + drawCommands.Length);
            Array.Copy(drawCommands, 0, DrawCommands, prevCmdLength, drawCommands.Length);

            for (int i = 0; i < drawCommands.Length; i++)
            {
                // TODO: Fix calculation of base instance to account for more than 1 instance per model
                DrawCommands[prevCmdLength + i].BaseInstance += prevCmdLength;
                DrawCommands[prevCmdLength + i].BaseVertex += prevBaseVertex;
                DrawCommands[prevCmdLength + i].FirstIndex += prevIndicesLength;
            }
        }

        private void LoadMeshes(GLSLMesh[] meshes)
        {
            int prevMeshesLength = Meshes.Length;
            int prevMaterialsLength = Materials.Length;
            Array.Resize(ref Meshes, prevMeshesLength + meshes.Length);
            Array.Copy(meshes, 0, Meshes, prevMeshesLength, meshes.Length);

            for (int i = 0; i < meshes.Length; i++)
            {
                Meshes[prevMeshesLength + i].MaterialIndex += prevMaterialsLength;
            }
        }

        private void LoadModelMatrices(Matrix4[][] matrices)
        {
            int prevMatricesLength = ModelMatrices.Length;
            Array.Resize(ref ModelMatrices, prevMatricesLength + matrices.Length);
            for (int i = 0; i < matrices.Length; i++)
            {
                ModelMatrices[prevMatricesLength + i] = new Matrix4[matrices[i].Length];
                Array.Copy(matrices[i], 0, ModelMatrices[prevMatricesLength + i], 0, matrices[i].Length);
            }
        }

        private void LoadMaterials(GLSLMaterial[] materials)
        {
            int prevMaterialsLength = Materials.Length;
            Array.Resize(ref Materials, prevMaterialsLength + materials.Length);
            Array.Copy(materials, 0, Materials, prevMaterialsLength, materials.Length);
        }
        private void LoadIndices(uint[] indices)
        {
            int prevIndicesLength = Indices.Length;
            Array.Resize(ref Indices, prevIndicesLength + indices.Length);
            Array.Copy(indices, 0, Indices, prevIndicesLength, indices.Length);
        }
        private void LoadVertices(GLSLDrawVertex[] vertices)
        {
            int prevVerticesLength = Vertices.Length;
            Array.Resize(ref Vertices, prevVerticesLength + vertices.Length);
            Array.Copy(vertices, 0, Vertices, prevVerticesLength, vertices.Length);
        }
    }
}
