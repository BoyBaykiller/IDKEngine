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
        public int MeshletsOffset;

        public Vector3 AbsorbanceBias;
        public int MeshletCount;

        public int InstanceCount;
        public bool TintOnTransmissive = true;
        private fixed bool _pad[3];
        private readonly float _pad0;
        public readonly float _pad1;

        public GpuMesh()
        {
        }
    }
}
