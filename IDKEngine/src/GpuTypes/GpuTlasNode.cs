using OpenTK.Mathematics;
using System.Diagnostics;

namespace IDKEngine.GpuTypes
{
    public struct GpuTlasNode
    {
        // TODO: Store instance id in here

        public Vector3 Min;
        private uint isLeafAndLeftChildOrInstanceID;

        public Vector3 Max;
        public uint BlasIndex;

        public bool IsLeaf
        {
            get => isLeafAndLeftChildOrInstanceID >> 31 == 1;

            set
            {
                uint isLeafNum = value ? 1u : 0u;
                isLeafAndLeftChildOrInstanceID = (isLeafNum << 31) | LeftChildOrInstanceID;
            }
        }

        public uint LeftChildOrInstanceID
        {
            get
            {
                const uint mask = (1u << 31) - 1;
                return isLeafAndLeftChildOrInstanceID & mask;
            }

            set
            {
                Debug.Assert(value <= (1u << 31 - 1));
                isLeafAndLeftChildOrInstanceID |= value;
            }
        }
    }
}
