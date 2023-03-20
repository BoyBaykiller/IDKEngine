using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GLSLLight
    {
        public Vector3 Position;
        public float Radius;
        public Vector3 Color;
        public int PointShadowIndex;
        public GLSLLight(Vector3 position, Vector3 color, float radius, int pointShadowIndex = -1)
        {
            Position = position;
            Color = color;
            Radius = radius;
            PointShadowIndex = pointShadowIndex;
        }

        public bool HasPointShadow()
        {
            return PointShadowIndex >= 0;
        }
    }
}
