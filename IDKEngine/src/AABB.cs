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

        public void Grow(in GLSLTriangle tri)
        {
            Grow(tri.Vertex0.Position);
            Grow(tri.Vertex1.Position);
            Grow(tri.Vertex2.Position);
        }

        public float Area()
        {
            Vector3 size = Max - Min;
            return 2 * (size.X * size.Y + size.X * size.Z + size.Z * size.Y);
        }

        public void Transform(Matrix4 model)
        {
            for (int i = 0; i < 8; i++)
            {
                bool isX = (i & 1) == 0 ? false : true;
                bool isY = (i & 2) == 0 ? false : true;
                bool isZ = (i & 4) == 0 ? false : true;
                Grow((new Vector4(isX ? Max.X : Min.X, isY ? Max.Y : Min.Y, isZ ? Max.Z : Min.Z, 1.0f) * model).Xyz);
            }
        }
    }
}
