using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GpuWavefrontRay
    {
        public Vector3 Origin;
        public uint DebugNodeCounter;

        public Vector3 Direction;
        public float PreviousIOR;

        public Vector3 Throughput;
        private readonly float _pad0;

        public Vector3 Radiance;
        private readonly float _pad1;
    }
}
