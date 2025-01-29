using System.Runtime.Intrinsics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.GpuTypes;

namespace IDKEngine.Shapes
{
    [StructLayout(LayoutKind.Explicit)]
    public record struct Box
    {
        public readonly Vector3 this[int index]
        {
            get
            {
                System.Diagnostics.Debug.Assert(index < 8);
                bool isMaxX = (index & 1) != 0;
                bool isMaxY = (index & 2) != 0;
                bool isMaxZ = (index & 4) != 0;
                return new Vector3(isMaxX ? Max.X : Min.X, isMaxY ? Max.Y : Min.Y, isMaxZ ? Max.Z : Min.Z);
            }
        }


        [FieldOffset(0)] public Vector3 Min;
        [FieldOffset(16)] public Vector3 Max;

        [FieldOffset(0)] public Vector128<float> SimdMin;
        [FieldOffset(16)] public Vector128<float> SimdMax;

        public Box(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public void GrowToFit(in Vector128<float> point)
        {
            SimdMin = Vector128.MinNative(SimdMin, point);
            SimdMax = Vector128.MaxNative(SimdMax, point);
        }

        public void GrowToFit(in Box box)
        {
            SimdMin = Vector128.MinNative(SimdMin, box.SimdMin);
            SimdMax = Vector128.MaxNative(SimdMax, box.SimdMax);
        }

        public void GrowToFit(in GpuTlasNode node)
        {
            // This overload only exists because converting from Node to Box imposed significant overhead for some reason

            Vector128<float> p0 = Vector128.Create(node.Min.X, node.Min.Y, node.Min.Z, 0.0f);
            Vector128<float> p1 = Vector128.Create(node.Max.X, node.Max.Y, node.Max.Z, 0.0f);
            
            SimdMin = Vector128.MinNative(SimdMin, p0);
            SimdMax = Vector128.MaxNative(SimdMax, p1);
        }

        public void GrowToFit(Vector3 point)
        {
            Vector128<float> p = Vector128.Create(point.X, point.Y, point.Z, 0.0f);
            GrowToFit(p);
        }

        public void GrowToFit(in Triangle tri)
        {
            GrowToFit(tri.Position0);
            GrowToFit(tri.Position1);
            GrowToFit(tri.Position2);
        }

        public readonly Vector3 Center()
        {
            return (Max + Min) * 0.5f;
        }

        public readonly Vector3 Size()
        {
            return Max - Min;
        }

        public readonly Vector3 HalfSize()
        {
            return Size() * 0.5f;
        }

        public readonly float Volume()
        {
            Vector3 size = Size();
            return size.X * size.Y * size.Z;
        }

        public readonly float HalfArea()
        {
            Vector3 size = Size();
            float area = (size.X + size.Y) * size.Z + size.X * size.Y;
            return area;
        }

        public void Transform(in Matrix4 matrix)
        {
            this = Transformed(this, matrix);
        }

        public static Box Transformed(in Box box, in Matrix4 matrix)
        {
            // TODO: This function is unreasonable slow in debugger. The indexer and the matrix muls take time
            Box newBox = Empty();
            for (int i = 0; i < 8; i++)
            {
                newBox.GrowToFit((new Vector4(box[i], 1.0f) * matrix).Xyz);
            }
            return newBox;
        }

        public static Vector3 GetOverlappingExtends(in Box a, in Box b)
        {
            Box boundingBox = a;
            boundingBox.GrowToFit(b);

            Vector3 addedSize = a.Size() + b.Size();
            Vector3 extends = addedSize - boundingBox.Size();

            return extends;
        }

        // Does not get inlined even though it's small and tends be a size decreasing inline
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Box Empty()
        {
            Box box = new Box();
            box.SimdMin = Vector128.Create<float>(float.MaxValue);
            box.SimdMax = Vector128.Create<float>(float.MinValue);
            return box;
        }

        public static Box From(in Triangle triangle)
        {
            Box box = new Box(triangle.Position0, triangle.Position0);
            box.GrowToFit(triangle.Position1);
            box.GrowToFit(triangle.Position2);

            return box;
        }
    }
}
