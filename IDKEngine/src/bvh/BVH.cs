using System;
using OpenTK.Mathematics;
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
            BVHBuffer.ImmutableAllocate(Vector4.SizeInBytes + blas.Nodes.Length * sizeof(GLSLNode), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.SubData(Vector2.SizeInBytes, 2 * sizeof(uint), new uint[] { (uint)blas.TreeDepth, BLAS.BITS_FOR_MISS_LINK });
            BVHBuffer.SubData(Vector4.SizeInBytes, blas.Nodes.Length * sizeof(GLSLNode), blas.Nodes);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);

            VertexBuffer = new BufferObject();
            VertexBuffer.ImmutableAllocate(sizeof(GLSLBLASVertex) * blas.Vertices.Length, blas.Vertices, BufferStorageFlags.DynamicStorageBit);
            VertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, VertexBuffer.Size);
        }

    }
}
