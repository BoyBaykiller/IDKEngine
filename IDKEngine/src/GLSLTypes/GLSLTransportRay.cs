using OpenTK.Mathematics;

namespace IDKEngine
{
    unsafe struct GLSLTransportRay
    {
        public Vector3 Origin;
        private readonly float _pad0;

        public Vector3 Direction;
        public float CurrentIOR;

        public Vector3 Throughput;
        public uint DebugFirstHitInteriorNodeCounter;

        public Vector3 Radiance;
        public bool IsRefractive;
        private fixed bool _pad1[3];
    }
}
