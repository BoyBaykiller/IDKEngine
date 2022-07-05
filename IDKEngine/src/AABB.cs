using OpenTK.Mathematics;

namespace IDKEngine
{
    struct AABB
    {
        public Vector3 Center => (Max + Min) * 0.5f;
        public Vector3 HalfSize => (Max - Min) * 0.5f;
        public Vector3 this[uint vertex]
        {
            get
            {
                System.Diagnostics.Debug.Assert(vertex < 8);
                bool isMaxX = (vertex & 1) == 0 ? false : true;
                bool isMaxY = (vertex & 2) == 0 ? false : true;
                bool isMaxZ = (vertex & 4) == 0 ? false : true;
                return new Vector3(isMaxX ? Max.X : Min.X, isMaxY ? Max.Y : Min.Y, isMaxZ ? Max.Z : Min.Z);
            }
        }

        public Vector3 Min;
        public Vector3 Max;
        public AABB()
        {
            Min = new Vector3(float.MaxValue);
            Max = new Vector3(float.MinValue);
        }

        public AABB(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
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

        public void Transform(Matrix4 invModel)
        {
            for (uint i = 0; i < 8; i++)
            {
                Grow((new Vector4(this[i], 1.0f) * invModel).Xyz);
            }
        }
    }
}
