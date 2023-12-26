using System.Numerics;

namespace IDKEngine
{
    public struct GpuMeshlet
    {
        public uint VertexOffset;
        public uint IndicesOffset;

        public byte VertexCount;
        public byte TriangleCount;
    };
}
