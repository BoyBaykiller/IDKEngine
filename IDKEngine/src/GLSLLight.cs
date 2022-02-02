using OpenTK;

namespace IDKEngine
{
    struct GLSLLight
    {
        public Vector3 Position;
        public float Radius;
        public Vector3 Color;
        private readonly float _pad0;
        public GLSLLight(Vector3 position, Vector3 color, float radius)
        {
            Position = position;
            Color = color;
            Radius = radius;
            _pad0 = 0;
        }

        public Matrix4 Translation => Matrix4.CreateScale(Radius) * Matrix4.CreateTranslation(Position);
    }
}
