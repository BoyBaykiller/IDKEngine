namespace IDKEngine.GpuTypes
{
    public record struct GpuMeshlet
    {
        public uint VertexOffset;
        public uint IndicesOffset;

        public byte VertexCount;
        public byte TriangleCount;
    };
}
