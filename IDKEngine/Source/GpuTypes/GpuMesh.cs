using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuMesh
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
        public uint BlasRootNodeIndex;
        public Vector2 _pad0;
    }
}
