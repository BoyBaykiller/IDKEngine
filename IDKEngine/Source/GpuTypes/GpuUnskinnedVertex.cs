using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

public record struct GpuUnskinnedVertex
{
    public Vector4i JointIndices;
    public Vector4 JointWeights;
    public Vector3 Position;
    public uint Tangent;
    public uint Normal;
}
