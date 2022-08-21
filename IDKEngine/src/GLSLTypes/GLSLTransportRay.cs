using OpenTK.Mathematics;

namespace IDKEngine
{
    unsafe struct GLSLTransportRay
    {
        public Vector3 Origin;
        public uint Direction;

        public Vector3 Throughput;
        public float PrevIOROrDebugNodeCounter;

        public Vector3 Radiance;
        public bool IsRefractive;
        private fixed bool _pad0[3];
    }
}
