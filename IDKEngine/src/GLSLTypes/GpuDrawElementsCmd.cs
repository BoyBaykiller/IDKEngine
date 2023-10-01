namespace IDKEngine
{
    public struct GpuDrawElementsCmd
    {
        public int Count;
        public int InstanceCount;
        public int FirstIndex;
        public int BaseVertex;
        public int BaseInstance;

        public uint BlasRootNodeIndex;
    }
}
