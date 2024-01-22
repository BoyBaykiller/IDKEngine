using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuMaterial
    {
        public Vector3 EmissiveFactor;
        public uint BaseColorFactor;

        public float TransmissionFactor;
        public float AlphaCutoff;
        public float RoughnessFactor;
        public float MetallicFactor;

        public Vector3 Absorbance;
        public float IOR;

        public ulong BaseColorTextureHandle;
        public ulong MetallicRoughnessTextureHandle;

        public ulong NormalTextureHandle;
        public ulong EmissiveTextureHandle;
    }
}
