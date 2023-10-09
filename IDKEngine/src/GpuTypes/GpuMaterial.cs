using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GpuMaterial
    {
        public Vector3 EmissiveFactor;
        public uint BaseColorFactor;

        private readonly float _pad0;
        public float AlphaCutoff;
        public float RoughnessFactor;
        public float MetallicFactor;

        public ulong BaseColorTextureHandle;
        public ulong MetallicRoughnessTextureHandle;

        public ulong NormalTextureHandle;
        public ulong EmissiveTextureHandle;
    }
}
