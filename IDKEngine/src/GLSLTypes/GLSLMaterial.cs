using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLMaterial
    {
        public const int TEXTURE_COUNT = 4;

        public Vector3 EmissiveFactor;
        public uint BaseColorFactor;

        public float RoughnessFactor;
        public float MetallicFactor;
        private readonly Vector2 _pad0;
        

        public ulong BaseColorTextureHandle;
        public ulong MetallicRoughnessTextureHandle;

        public ulong NormalTextureHandle;
        public ulong EmissiveTextureHandle;
    }
}
