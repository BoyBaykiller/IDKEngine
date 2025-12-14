using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;

namespace IDKEngine.GpuTypes;

public record struct GpuBlasNode
{
    public readonly bool IsLeaf => TriCount > 0;

    public readonly int TriEnd
    {
        get
        {
            return TriStartOrChild + TriCount;
        }
    }

    public Vector3 Min;
    public int TriStartOrChild;
    public Vector3 Max;
    public int TriCount;

    public void SetBounds(in Box box)
    {
        Min = box.Min;
        Max = box.Max;
    }

    public readonly float HalfArea()
    {
        Vector3 size = Max - Min;
        float area = MyMath.HalfArea(size.X, size.Y, size.Z);

        return area;
    }
}
