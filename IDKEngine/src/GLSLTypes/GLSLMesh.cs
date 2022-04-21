using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLMesh
    {
        public Matrix4 Model;
        //public int BaseMatrix;
        //public int InstanceCount;
        public int MaterialIndex;
        public int NodeStart;
        public int BLASDepth;
        public float Emissive;
        public float NormalMapStrength;
        public float SpecularChance;
        public float Roughness;
        public float RefractionChance;
    }
}
