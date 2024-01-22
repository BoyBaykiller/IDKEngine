using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    class GpuLightWrapper
    {
        public GpuLight GpuLight;

        public GpuLightWrapper(float radius)
        {
            GpuLight.Radius = radius;
            GpuLight.PointShadowIndex = -1;
        }

        public GpuLightWrapper(Vector3 position, Vector3 color, float radius)
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
