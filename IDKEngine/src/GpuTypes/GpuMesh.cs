using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GpuMesh
    {
        public int InstanceCount;
        public int MaterialIndex;
        public float NormalMapStrength;
        public float EmissiveBias;

        public float SpecularBias;
        public float RoughnessBias;
        public float RefractionChance;
        public float IOR;

        public Vector3 Absorbance;
        private uint cubemapShadowCullInfo;
    }
}
