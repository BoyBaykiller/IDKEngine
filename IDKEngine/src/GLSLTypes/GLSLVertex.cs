using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLVertex
    {
        public Vector3 Position;
        private readonly float _pad0;
        public Vector2 TexCoord;
        private readonly Vector2 _pad1;
        public Vector3 Normal;
        private readonly float _pad2;
    }
}
