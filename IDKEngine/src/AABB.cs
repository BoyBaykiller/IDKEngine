using OpenTK.Mathematics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

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

        public AABB(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public void Shrink(in Vector3 point)
        {
            Vector128<float> p = Vector128.Create(point.X, point.Y, point.Z, 0.0f);
            Vector128<float> min = Vector128.Create(Min.X, Min.Y, Min.Z, 0.0f);
            Vector128<float> max = Vector128.Create(Max.X, Max.Y, Max.Z, 0.0f);

            Min = Sse.Min(min, p).AsVector3().ToOpenTK();
            Max = Sse.Max(max, p).AsVector3().ToOpenTK();
        }

        public void GrowToFit(in AABB aaab)
        {
            Shrink(aaab.Min);
            Shrink(aaab.Max);
        }

        public void GrowToFit(in GLSLTriangle tri)
        {
            Shrink(tri.Vertex0.Position);
            Shrink(tri.Vertex1.Position);
            Shrink(tri.Vertex2.Position);
        }

        public float Area()
        {
            Vector3 size = Max - Min;
            return 2.0f * (size.X * size.Y + size.X * size.Z + size.Z * size.Y);
        }

        public void Transform(Matrix4 model)
        {
            AABB transformed = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (uint i = 0; i < 8; i++)
            {
                transformed.Shrink((new Vector4(this[i], 1.0f) * model).Xyz);
            }
            this = transformed;
        }
    }
}
