using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GpuMesh
    {
        public int MaterialIndex;
        public float NormalMapStrength;
        public float EmissiveBias;

        public float SpecularBias;
        public float RoughnessBias;
        public float RefractionChance;
        public float IOR;
        private readonly int _pad0;

        public Vector3 Absorbance;
        private uint cubemapShadowCullInfo;
    }
}
