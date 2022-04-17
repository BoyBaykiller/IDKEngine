using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLMesh
    {
        public Matrix4 Model;
        public int MaterialIndex;
        public int BaseIndex;
        public float Emissive;
        public float NormalMapStrength;
        public float SpecularChance;
        public float Roughness;
        public int BLASDepth;
        private readonly float _pad1;
    }
}
