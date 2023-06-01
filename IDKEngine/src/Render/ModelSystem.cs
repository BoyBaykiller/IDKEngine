using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ModelSystem : IDisposable
    {
        public GLSLDrawElementsCmd[] DrawCommands;
        private readonly BufferObject drawCommandBuffer;

        public GLSLMesh[] Meshes;
        private readonly BufferObject meshBuffer;

        public GLSLMeshInstance[] MeshInstances;
        private readonly BufferObject meshInstanceBuffer;

        public GLSLMaterial[] Materials;
        private readonly BufferObject materialBuffer;

        public GLSLDrawVertex[] Vertices;
        private readonly BufferObject vertexBuffer;

        public uint[] Indices;
        private readonly BufferObject elementBuffer;


        private readonly VAO vao;
        private readonly ShaderProgram frustumCullingProgram;
        public unsafe ModelSystem()
        {
            DrawCommands = Array.Empty<GLSLDrawElementsCmd>();
            drawCommandBuffer = new BufferObject();
            drawCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0);

            Meshes = Array.Empty<GLSLMesh>();
            meshBuffer = new BufferObject();
            meshBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1);

            MeshInstances = Array.Empty<GLSLMeshInstance>();
            meshInstanceBuffer = new BufferObject();
            meshInstanceBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 2);

            Materials = Array.Empty<GLSLMaterial>();
            materialBuffer = new BufferObject();
            materialBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3);

            Vertices = Array.Empty<GLSLDrawVertex>();
            vertexBuffer = new BufferObject();

            Indices = Array.Empty<uint>();
            elementBuffer = new BufferObject();

            vao = new VAO();
            vao.SetElementBuffer(elementBuffer);
            vao.AddSourceBuffer(vertexBuffer, 0, sizeof(GLSLDrawVertex));
            vao.SetAttribFormat(0, 0, 3, VertexAttribType.Float, sizeof(float) * 0); // Position
            vao.SetAttribFormat(0, 1, 2, VertexAttribType.Float, sizeof(float) * 4); // TexCoord
            vao.SetAttribFormatI(0, 2, 1, VertexAttribType.UnsignedInt, sizeof(float) * 6); // Tangent
            vao.SetAttribFormatI(0, 3, 1, VertexAttribType.UnsignedInt, sizeof(float) * 7); // Normal

            frustumCullingProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/SingleView/Frustum/compute.glsl")));
        }

        public unsafe void Add(params Model[] models)
        {
            if (models.Length == 0)
            {
                return;
            }

            for (int i = 0; i < models.Length; i++)
            {
                // Don't modify order
                LoadDrawCommands(models[i].DrawCommands);
                LoadVertices(models[i].Vertices);

                // Don't modify order
                LoadMeshes(models[i].Meshes);
                LoadMaterials(models[i].Materials);

                LoadIndices(models[i].Indices);
                LoadModelMatrices(models[i].MeshInstances);
            }

            drawCommandBuffer.MutableAllocate(DrawCommands.Length * sizeof(GLSLDrawElementsCmd), DrawCommands);
            meshBuffer.MutableAllocate(Meshes.Length * sizeof(GLSLMesh), Meshes);
            materialBuffer.MutableAllocate(Materials.Length * sizeof(GLSLMaterial), Materials);
            vertexBuffer.MutableAllocate(Vertices.Length * sizeof(GLSLDrawVertex), Vertices);
            elementBuffer.MutableAllocate(Indices.Length * sizeof(uint), Indices);
            meshInstanceBuffer.MutableAllocate(MeshInstances.Length * sizeof(GLSLMeshInstance), (IntPtr)0);

            UpdateMeshInstanceBuffer(0, MeshInstances.Length);
        }

        public unsafe void Draw()
        {
            vao.Bind();
            drawCommandBuffer.Bind(BufferTarget.DrawIndirectBuffer);
            GL.MultiDrawElementsIndirect(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, 0, Meshes.Length, sizeof(GLSLDrawElementsCmd));
        }

        public unsafe void FrustumCull(in Matrix4 projView)
        {
            frustumCullingProgram.Use();
            frustumCullingProgram.Upload(0, projView);

            GL.DispatchCompute((Meshes.Length + 64 - 1) / 64, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
        }


        public unsafe void UpdateMeshBuffer(int start, int count)
        {
            if (count == 0) return;
            meshBuffer.SubData(start * sizeof(GLSLMesh), count * sizeof(GLSLMesh), Meshes[start]);
        }

        public unsafe void UpdateDrawCommandBuffer(int start, int count)
        {
            if (count == 0) return;
            drawCommandBuffer.SubData(start * sizeof(GLSLDrawElementsCmd), count * sizeof(GLSLDrawElementsCmd), DrawCommands[start]);
        }

        public unsafe void UpdateMeshInstanceBuffer(int start, int count)
        {
            if (count == 0) return;
            meshInstanceBuffer.SubData(start * sizeof(GLSLMeshInstance), count * sizeof(GLSLMeshInstance), MeshInstances[start]);
        }

        private void LoadDrawCommands(GLSLDrawElementsCmd[] drawCommands)
        {
            int prevCmdLength = DrawCommands.Length;
            int prevIndicesLength = DrawCommands.Length == 0 ? 0 : DrawCommands[prevCmdLength - 1].FirstIndex + DrawCommands[prevCmdLength - 1].Count;
            int prevBaseVertex = DrawCommands.Length == 0 ? 0 : DrawCommands[prevCmdLength - 1].BaseVertex + GetMeshVertexCount(prevCmdLength - 1);

            Array.Resize(ref DrawCommands, prevCmdLength + drawCommands.Length);

            Array.Copy(drawCommands, 0, DrawCommands, prevCmdLength, drawCommands.Length);

            for (int i = 0; i < drawCommands.Length; i++)
            {
                // TODO: Fix calculation of base instance to account for more than 1 instance per gltfModel
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
        private void LoadModelMatrices(GLSLMeshInstance[] matrices)
        {
            int prevMatricesLength = MeshInstances.Length;
            Array.Resize(ref MeshInstances, prevMatricesLength + matrices.Length);
            Array.Copy(matrices, 0, MeshInstances, prevMatricesLength, matrices.Length);
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

        public int GetMeshVertexCount(int meshIndex)
        {
            return ((meshIndex + 1 > DrawCommands.Length - 1) ? Vertices.Length : DrawCommands[meshIndex + 1].BaseVertex) - DrawCommands[meshIndex].BaseVertex;
        }

        public void Dispose()
        {
            drawCommandBuffer.Dispose();
            meshBuffer.Dispose();
            materialBuffer.Dispose();
            vertexBuffer.Dispose();
            elementBuffer.Dispose();
            meshInstanceBuffer.Dispose();

            vao.Dispose();

            frustumCullingProgram.Dispose();
        }
    }
}
