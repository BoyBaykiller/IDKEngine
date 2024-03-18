using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuBlasNode
    {
        public Vector3 Min;
        public uint TriStartOrChild;
        public Vector3 Max;
        public uint TriCount;

        public bool IsLeaf => TriCount > 0;
    }
}
