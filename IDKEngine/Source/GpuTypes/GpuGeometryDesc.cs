namespace IDKEngine.GpuTypes;

public record struct GpuGeometryDesc
{
    public readonly int TrianglesEnd => TriangleOffset + TriangleCount;
    public readonly int VerticesEnd => VertexOffset + VertexCount;

    public int TriangleOffset;
    public int TriangleCount;
    public int VertexOffset;
    public int VertexCount;
}
