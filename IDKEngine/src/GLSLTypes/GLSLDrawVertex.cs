using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLDrawVertex
    {
        public Vector3 Position;
        public float TexCoordU;

        public Vector3 Normal;
        public float TexCoordV;

        public Vector3 Tangent;
        private readonly float _pad0;
    }
}
