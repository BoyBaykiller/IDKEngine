using BBOpenGL;

namespace IDKEngine.GpuTypes
{
    public struct GpuBindlessGBuffer
    {
        public BBG.Texture.BindlessHandle AlbedoAlphaTexture;
        public BBG.Texture.BindlessHandle NormalTexture;
        public BBG.Texture.BindlessHandle MetallicRoughnessTexture;
        public BBG.Texture.BindlessHandle EmissiveTexture;
        public BBG.Texture.BindlessHandle VelocityTexture;
        public BBG.Texture.BindlessHandle DepthTexture;
    }
}
