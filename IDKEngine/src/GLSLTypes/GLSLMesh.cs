using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLMesh
    {
        public Matrix4 Model;
        public Matrix4 PrevModel;
        public int MaterialIndex;
        public int BaseNode;
        public float Emissive;
        private readonly float _pad1;
    }
}
