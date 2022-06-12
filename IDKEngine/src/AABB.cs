using OpenTK.Mathematics;

namespace IDKEngine
{
    struct AABB
    {
        public Vector3 Min;
        public Vector3 Max;
        public AABB()
        {
            Min = new Vector3(float.MaxValue);
            Max = new Vector3(float.MinValue);
        }

        public void Grow(in Vector3 value)
        {
            Min = Vector3.ComponentMin(Min, value);
            Max = Vector3.ComponentMax(Max, value);
        }

        public void Grow(in AABB aaab)
        {
            Grow(aaab.Min);
            Grow(aaab.Max);
        }

        public void Grow(in GLSLTriangle aaab)
        {
            Grow(aaab.Vertex0.Position);
            Grow(aaab.Vertex1.Position);
            Grow(aaab.Vertex2.Position);
        }

        public float Area()
        {
            Vector3 size = Max - Min;
            return 2 * (size.X * size.Y + size.X * size.Z + size.Z * size.Y);
        }
    }
}
