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
        private readonly float pad;
        private readonly float alsoPad;
        private readonly float _pad0;
        private readonly float _pad1;
    }
}
