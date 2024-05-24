namespace IDKEngine.GpuTypes
{
    public struct GpuGBuffer
    {
        public ulong AlbedoAlphaTextureHandle;
        public ulong NormalTextureHandle;
        public ulong MetallicRoughnessTextureHandle;
        public ulong EmissiveTextureHandle;
        public ulong VelocityTextureHandle;
        public ulong DepthTextureHandle;
    }
}
