using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class BVH
    {
        private readonly BufferObject BVHBuffer;
        private readonly BufferObject TriangleBuffer;
        public unsafe BVH(BLAS blas)
        {
            BVHBuffer = new BufferObject();
            BVHBuffer.ImmutableAllocate(sizeof(GLSLBlasNode) * blas.Nodes.Length, blas.Nodes, BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);

            TriangleBuffer = new BufferObject();
            TriangleBuffer.ImmutableAllocate(sizeof(GLSLTriangle) * blas.Triangles.Length, blas.Triangles, BufferStorageFlags.DynamicStorageBit);
            TriangleBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, TriangleBuffer.Size);
        }
    }
}
