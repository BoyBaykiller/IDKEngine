namespace IDKEngine
{
    public struct GLSLMaterial
    {
        public readonly long Albedo;
        private readonly long _pad0;

        public readonly long Normal;
        private readonly long _pad1;

        public readonly long Roughness;
        private readonly long _pad3;

        public readonly long Specular;
        private readonly long _pad4;
    }
}
