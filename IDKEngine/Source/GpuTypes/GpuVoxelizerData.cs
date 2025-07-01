using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

record struct GpuVoxelizerData
{
    public Vector3 GridMin = new Vector3(float.MinValue);
    private readonly float _pad0;
    public Vector3 GridMax = new Vector3(float.MaxValue);
    private readonly float _pad1;

    public GpuVoxelizerData()
    {
    }
}
