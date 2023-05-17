using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace IDKEngine
{
    [StructLayout(LayoutKind.Explicit)]
    struct GLSLTlasNode
    {
        [FieldOffset(0)] public Vector3 Min;
        [FieldOffset(12)] public ushort RightChild;
        [FieldOffset(14)] public ushort LeftChild;

        [FieldOffset(16)] public Vector3 Max;
        [FieldOffset(28)] public uint BlasIndex;

        public bool IsLeaf()
        {
            return LeftChild == 0 && RightChild == 0;
        }
    }
}
