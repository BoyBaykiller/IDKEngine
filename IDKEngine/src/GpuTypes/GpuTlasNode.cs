using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GpuTlasNode
    {
        // TODO: Store instance id in here

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
