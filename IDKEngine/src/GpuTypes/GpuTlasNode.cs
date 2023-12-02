using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GpuTlasNode
    {
        public Vector3 Min;
        public uint LeftChild;

        public Vector3 Max;
        public uint BlasIndex;

        public bool IsLeaf()
        {
            return LeftChild == 0;
        }
    }
}
