namespace IDKEngine
{
    public struct GpuDrawElementsCmd
    {
        public int IndexCount;
        public int InstanceCount;
        public int FirstIndex;
        public int BaseVertex;
        public int BaseInstance;

        public uint BlasRootNodeIndex;
    }
}
