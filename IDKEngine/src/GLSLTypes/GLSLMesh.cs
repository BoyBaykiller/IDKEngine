using OpenTK;

namespace IDKEngine
{
    public struct GLSLMesh
    {
        public Matrix4 Model;
        public Matrix4 PrevModel;
        public int MaterialIndex;
        public int BaseNode;
        private readonly float _pad0;
        private readonly float _pad1;
    }
}
