using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public unsafe struct GpuMesh
    {
        public int MaterialId;
        public float NormalMapStrength;
        public float EmissiveBias;
        public float SpecularBias;

        public float RoughnessBias;
        public float TransmissionBias;
        public float IORBias;
        public int MeshletsStart;

        public Vector3 AbsorbanceBias;
        public int MeshletCount;

        public int InstanceCount;
        public int BlasRootNodeOffset;
        public bool TintOnTransmissive = true;
        private fixed bool _pad[3];
        private readonly float _pad0;

        public GpuMesh()
        {
        }
    }
}
