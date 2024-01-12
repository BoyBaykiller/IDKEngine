using OpenTK.Mathematics;

namespace IDKEngine
{
    class Light
    {
        public GpuLight GpuLight;

        public Light(float radius)
        {
            GpuLight.Radius = radius;
            GpuLight.PointShadowIndex = -1;
        }

        public Light(Vector3 position, Vector3 color, float radius)
        {
            GpuLight.Position = position;
            GpuLight.Color = color;
            GpuLight.Radius = radius;
            GpuLight.PointShadowIndex = -1;
        }

        public bool HasPointShadow()
        {
            return GpuLight.PointShadowIndex >= 0;
        }
    }
}
