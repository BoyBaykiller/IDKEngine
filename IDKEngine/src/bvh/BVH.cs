using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class BVH
    {
        private readonly BufferObject BVHBuffer;
        private readonly BufferObject VertexBuffer;
        public unsafe BVH(BLAS blas)
        {
            BVHBuffer = new BufferObject();
            BVHBuffer.ImmutableAllocate(sizeof(GLSLBlasNode) * blas.Nodes.Sum(arr => arr.Length), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);

            VertexBuffer = new BufferObject();
            VertexBuffer.ImmutableAllocate(sizeof(GLSLVertex) * blas.Vertices.Sum(arr => arr.Length), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            VertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, VertexBuffer.Size);

            int nodesOffset = 0, verticesOffset = 0;
            for (int i = 0; i < blas.Nodes.Length; i++)
            {
                BVHBuffer.SubData(sizeof(GLSLBlasNode) * nodesOffset, sizeof(GLSLBlasNode) * blas.Nodes[i].Length, blas.Nodes[i]);
                VertexBuffer.SubData(sizeof(GLSLVertex) * verticesOffset, sizeof(GLSLVertex) * blas.Vertices[i].Length, blas.Vertices[i]);

                nodesOffset += blas.Nodes[i].Length;
                verticesOffset += blas.Vertices[i].Length;
            }
        }
    }
}
