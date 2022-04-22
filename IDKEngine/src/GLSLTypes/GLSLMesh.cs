using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLMesh
    {
        public int InstanceCount;
        public int MatrixStart;
        public int NodeStart;
        public int BLASDepth;
        public int MaterialIndex;
        public float Emissive;
        public float NormalMapStrength;
        public float SpecularChance;
        public float Roughness;
        public float RefractionChance;
    }
}
