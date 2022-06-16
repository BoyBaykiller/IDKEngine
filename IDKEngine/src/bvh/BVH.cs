using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class BVH
    {
        private readonly BufferObject BVHBuffer;
        private readonly BufferObject TriangleBuffer;
        public unsafe BVH(ModelSystem modelSystem)
        {
            GLSLTriangle[] triangles = new GLSLTriangle[modelSystem.Indices.Length / 3];
            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                GLSLDrawCommand cmd = modelSystem.DrawCommands[i];
                for (int j = cmd.FirstIndex; j < cmd.FirstIndex + cmd.Count; j += 3)
                {
                    triangles[j / 3].Vertex0 = modelSystem.Vertices[modelSystem.Indices[j + 0] + cmd.BaseVertex];
                    triangles[j / 3].Vertex1 = modelSystem.Vertices[modelSystem.Indices[j + 1] + cmd.BaseVertex];
                    triangles[j / 3].Vertex2 = modelSystem.Vertices[modelSystem.Indices[j + 2] + cmd.BaseVertex];
                }
            }

            GLSLBlasNode[][] nodes = new GLSLBlasNode[modelSystem.Meshes.Length][];
            System.Threading.Tasks.Parallel.For(0, modelSystem.Meshes.Length, i =>
            {
                GLSLDrawCommand cmd = modelSystem.DrawCommands[i];
                int baseTriangleCount = cmd.FirstIndex / 3;
                BLAS blas;
                fixed (GLSLTriangle* ptr = triangles)
                {
                    blas = new BLAS(ptr + baseTriangleCount, cmd.Count / 3);
                }
                for (int j = 0; j < blas.Nodes.Length; j++)
                    if (blas.Nodes[j].TriCount > 0)
                        blas.Nodes[j].TriStartOrLeftChild += (uint)baseTriangleCount;

                nodes[i] = blas.Nodes;
            });

            int nodesCount = nodes.Sum(arr => arr.Length), nodesUploaded = 0;

            BVHBuffer = new BufferObject();
            BVHBuffer.ImmutableAllocate(sizeof(GLSLBlasNode) * nodesCount, (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);
            for (int i = 0; i < nodes.Length; i++)
            {
                BVHBuffer.SubData(nodesUploaded * sizeof(GLSLBlasNode), nodes[i].Length * sizeof(GLSLBlasNode), nodes[i]);
                nodesUploaded += nodes[i].Length;
            }

            TriangleBuffer = new BufferObject();
            TriangleBuffer.ImmutableAllocate(sizeof(GLSLTriangle) * triangles.Length, triangles, BufferStorageFlags.DynamicStorageBit);
            TriangleBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, TriangleBuffer.Size);
        }
    }
}
