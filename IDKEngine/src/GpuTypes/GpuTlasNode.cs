using OpenTK.Mathematics;
using System.Diagnostics;

namespace IDKEngine.GpuTypes
{
    public struct GpuTlasNode
    {
        public Vector3 Min;
        private uint isLeafAndChildOrInstanceID;

        public Vector3 Max;
        private readonly float _pad0;

        public bool IsLeaf
        {
            get => isLeafAndChildOrInstanceID >> 31 == 1;

            set
            {
                uint isLeafNum = value ? 1u : 0u;
                isLeafAndChildOrInstanceID = (isLeafNum << 31) | ChildOrInstanceID;
            }
        }
        public uint ChildOrInstanceID
        {
            get
            {
                const uint mask = (1u << 31) - 1;
                return isLeafAndChildOrInstanceID & mask;
            }

            set
            {
                Debug.Assert(value <= (1u << 31 - 1));
                isLeafAndChildOrInstanceID |= value;
            }
        }
    }
}
