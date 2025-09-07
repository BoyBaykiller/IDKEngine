namespace IDKEngine.GpuTypes;

public record struct GpuBlasDesc
{
    public readonly int NodesEnd => RootNodeOffset + NodeCount;
    public readonly int LeafIndicesEnd => LeafIndicesOffset + LeafIndicesCount;

    public GpuGeometryDesc GeometryDesc;

    public int RootNodeOffset;
    public int NodeCount;
    public int LeafIndicesOffset;
    public int LeafIndicesCount;

    public int MaxTreeDepth;
    public bool PreSplittingWasDone;
    private byte _pad0, _pad1, _pad2;
}
