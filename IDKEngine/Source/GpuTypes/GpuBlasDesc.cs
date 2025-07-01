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
    public int UnpaddedNodesCount;

    public bool PreSplittingWasDone;
}
