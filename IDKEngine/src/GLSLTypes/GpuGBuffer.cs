namespace IDKEngine
{
    struct GpuGBuffer
    {
        public ulong AlbedoAlphaTextureHandle;
        public ulong NormalSpecularTextureHandle;
        public ulong EmissiveRoughnessTextureHandle;
        public ulong VelocityTextureHandle;
        public ulong DepthTextureHandle;
    }
}
