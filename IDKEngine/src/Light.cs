using OpenTK.Mathematics;

namespace IDKEngine
{
    class Light
    {
        public GpuLight GLSLLight;
        public Light(Vector3 position, Vector3 color, float radius)
        {
            GLSLLight.Position = position;
            GLSLLight.Color = color;
            GLSLLight.Radius = radius;
            GLSLLight.PointShadowIndex = -1;
        }

        public bool HasPointShadow()
        {
            return GLSLLight.PointShadowIndex >= 0;
        }
    }
}
