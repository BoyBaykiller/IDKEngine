using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLMaterial
    {
        public const int TEXTURE_COUNT = 4;

        public Vector4 BaseColorFactor;

        public ulong AlbedoAlphaTextureHandle;
        public ulong MetallicRoughnessTextureHandle;

        public ulong NormalTextureHandle;
        public ulong EmissiveTextureHandle;
    }
}
