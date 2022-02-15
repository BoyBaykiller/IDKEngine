using OpenTK;

namespace IDKEngine
{
    struct GLSLBVHVertex
    {
        public Vector2 TexCoord;
        private readonly Vector2 _pad0;
        public Vector3 Normal;
        private readonly float _pad1;
        public Vector3 Tangent;
        private readonly float _pad2;
        public Vector3 BiTangent;
        private readonly float _pad3;
    }
}
