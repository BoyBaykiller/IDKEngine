using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

public unsafe struct GpuMesh
{
    public Vector3 LocalBoundsMin;
    public int MaterialId;

    public Vector3 LocalBoundsMax;
    public float NormalMapStrength;

    public Vector3 AbsorbanceBias;
    public int MeshletsOffset;

    public int MeshletCount;
    public float EmissiveBias;
    public float SpecularBias;
    public float RoughnessBias;

    public float TransmissionBias;
    public float IORBias;
    public int InstanceCount = 1;
    public int VertexCount;

    private readonly Vector3 _pad0;
    public bool TintOnTransmissive = true; // Required by KHR_materials_transmission
    private fixed byte _pad[3];

    public GpuMesh()
    {
    }
}
