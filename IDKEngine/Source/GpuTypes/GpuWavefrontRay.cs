using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    struct GpuWavefrontRay
    {
        public Vector3 Origin;
        public float PreviousIOROrDebugNodeCounter;

        public Vector3 Throughput;
        public float PackedDirectionX;

        public Vector3 Radiance;
        public float PackedDirectionY;
    }
}
