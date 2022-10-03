using OpenTK.Mathematics;

namespace IDKEngine
{
    unsafe struct GLSLTransportRay
    {
        public Vector3 Origin;
        private float _pad0;

        public Vector3 Direction;
        private float _pad1;

        public Vector3 Throughput;
        public float PrevIOROrDebugNodeCounter;

        public Vector3 Radiance;
        public bool IsRefractive;
        private fixed bool _pad2[3];
    }
}
