using OpenTK.Mathematics;
using IDKEngine.Shapes;

namespace IDKEngine.GpuTypes
{
    public record struct GpuBlasNode
    {
        public Vector3 Min;
        public uint TriStartOrChild;
        public Vector3 Max;
        public uint TriCount;

        public bool IsLeaf => TriCount > 0;

        public void SetBounds(in Box box)
        {
            Min = box.Min;
            Max = box.Max;
        }

        public float HalfArea()
        {
            Vector3 size = Max - Min;
            float area = (size.X + size.Y) * size.Z + size.X * size.Y;
            return area;
        }
    }
}
