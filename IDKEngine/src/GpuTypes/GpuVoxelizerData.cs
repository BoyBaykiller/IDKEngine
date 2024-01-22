using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    struct GpuVoxelizerData
    {
        public Matrix4 OrthoProjection;
        public Vector3 GridMin;
        private readonly float _pad0;
        public Vector3 GridMax;
        private readonly float _pad1;
    }
}
