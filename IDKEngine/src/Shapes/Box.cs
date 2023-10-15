using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using System;

namespace IDKEngine.Shapes
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Box
    {
        public Vector3 Center => (Max + Min) * 0.5f;
        public Vector3 HalfSize => (Max - Min) * 0.5f;
        public Vector3 this[int vertex]
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


        [FieldOffset(0)] public Vector3 Min;
        [FieldOffset(16)] public Vector3 Max;

        [FieldOffset(0)] public Vector128<float> SIMDMin;
        [FieldOffset(16)] public Vector128<float> SIMDMax;

        public Box(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public void GrowToFit(in Vector128<float> point)
        {
            SIMDMin = Sse.Min(SIMDMin, point);
            SIMDMax = Sse.Max(SIMDMax, point);
        }

        public void GrowToFit(in Vector3 point)
        {
            Vector128<float> p = Vector128.Create(point.X, point.Y, point.Z, 0.0f);
            GrowToFit(p);
        }

        public void GrowToFit(in Box box)
        {
            GrowToFit(box.SIMDMin);
            GrowToFit(box.SIMDMax);
        }

        public void GrowToFit(in GpuBlasTriangle tri)
        {
            GrowToFit(tri.Position0);
            GrowToFit(tri.Position1);
            GrowToFit(tri.Position2);
        }

        public void Transform(in Matrix4 model)
        {
            Box newBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < 8; i++)
            {
                newBox.GrowToFit((new Vector4(this[i], 1.0f) * model).Xyz);
            }
            this = newBox;
        }

        public static Box Transformed(Box box, in Matrix4 model)
        {
            box.Transform(model);
            return box;
        }
    }
}
