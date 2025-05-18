using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    record struct GpuWavefrontRay
    {
        public Vector3 Origin;
        public float PreviousIOROrTraverseCost;

        public Vector3 Throughput;
        public float PackedDirectionX;

        public Vector3 Radiance;
        public float PackedDirectionY;
    }
}
