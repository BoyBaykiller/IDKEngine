using OpenTK.Mathematics;

namespace IDKEngine
{
    unsafe struct GLSLTransportRay
    {
        public Vector3 Origin;
        public uint DebugNodeCounter;

        public Vector3 Direction;
        public float PreviousIOR;

        public Vector3 Throughput;
        public bool IsRefractive;

        public Vector3 Radiance;
        private float _pad0;
    }
}
