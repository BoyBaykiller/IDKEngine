using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

public record struct GpuVertex
{
    public Vector2 TexCoord;
    public uint Tangent;
    public uint Normal;
    public int MaterialId;
}
