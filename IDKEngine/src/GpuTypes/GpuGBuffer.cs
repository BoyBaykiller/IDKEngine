namespace IDKEngine.GpuTypes
{
    public struct GpuGBuffer
    {
        public long AlbedoAlphaTextureHandle;
        public long NormalSpecularTextureHandle;
        public long EmissiveRoughnessTextureHandle;
        public long VelocityTextureHandle;
        public long DepthTextureHandle;
    }
}
