using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

record struct GpuAovRay
{
    public Vector3 Albedo;
    public float NewWeight;
    public Vector3 Normal;
    private float _pad0;
}
