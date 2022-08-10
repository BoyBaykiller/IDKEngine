using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GLSLTransportRay
    {
        public Vector3 Origin;
        public uint IsRefractive;

        public Vector3 Direction;
        public float CurrentIOR;

        public Vector3 Throughput;
        public uint DebugFirstHitInteriorNodeCounter;

        public Vector3 Radiance;
        private readonly float _pad1;
    }
}
