using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public record struct GpuMesh
    {
        public int MaterialIndex;
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
        public Vector2 _pad0;
    }
}
