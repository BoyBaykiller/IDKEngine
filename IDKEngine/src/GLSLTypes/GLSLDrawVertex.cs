using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GLSLDrawVertex
    {
        public Vector3 Position;
        private readonly float _pad0;

        public Vector2 TexCoord;
        public uint Tangent;
        public uint Normal;
    }
}
