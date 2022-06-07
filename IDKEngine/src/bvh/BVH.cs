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
            BVHBuffer.ImmutableAllocate(sizeof(GLSLNode) * blas.Nodes.Length, blas.Nodes, BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);

            VertexBuffer = new BufferObject();
            VertexBuffer.ImmutableAllocate(sizeof(GLSLVertex) * blas.Vertices.Length, blas.Vertices, BufferStorageFlags.DynamicStorageBit);
            VertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, VertexBuffer.Size);
        }
    }
}
