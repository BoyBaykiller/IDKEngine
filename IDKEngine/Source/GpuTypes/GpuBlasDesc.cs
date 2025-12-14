namespace IDKEngine.GpuTypes;

public record struct GpuBlasDesc
{
    public readonly int NodesEnd => NodeOffset + NodeCount;
    public readonly int LeafIndicesEnd => LeafIndicesOffset + LeafIndicesCount;
    public readonly int ParentIndicesEnd => ParentIndicesOffset + ParentIndicesCount;
    public readonly int TrianglesEnd => TriangleOffset + TriangleCount;

    public int NodeOffset;
    public int NodeCount;
    public int TriangleOffset;
    public int TriangleCount;
    public int LeafIndicesOffset;
    public int LeafIndicesCount;
    public int ParentIndicesOffset;
    public int ParentIndicesCount;
    public int RequiredStackSize;
    public bool IsRefittable;
}
