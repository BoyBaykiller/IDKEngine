using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using System;

namespace IDKEngine.Shapes
{
    public record struct Box
    {
        public readonly Vector3 Min => SimdMin.ToOpenTK();
        public readonly Vector3 Max => SimdMax.ToOpenTK();

        public Vector128<float> SimdMin;
        public Vector128<float> SimdMax;

        public Box(Vector3 min, Vector3 max)
            : this(Vector128.Create(min.X, min.Y, min.Z, 0.0f), Vector128.Create(max.X, max.Y, max.Z, 0.0f))
        {
        }

        public Box(Vector128<float> min, Vector128<float> max)
        {
            SimdMin = min;
            SimdMax = max;
        }

        public readonly Vector3 this[int index]
        {
            get
            {
                Debug.Assert(index >= 0 && index < 8);
                bool isMaxX = (index & 1) != 0;
                bool isMaxY = (index & 2) != 0;
                bool isMaxZ = (index & 4) != 0;
                return new Vector3(isMaxX ? SimdMax[0] : SimdMin[0], isMaxY ? SimdMax[1] : SimdMin[1], isMaxZ ? SimdMax[2] : SimdMin[2]);
            }
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

        public void ShrinkToFit(in Box box)
        {
            SimdMin = Vector128.MaxNative(SimdMin, box.SimdMin);
            SimdMax = Vector128.MinNative(SimdMax, box.SimdMax);
        }

        public readonly Vector3 Center()
        {
            return ((SimdMax + SimdMin) * 0.5f).ToOpenTK();
        }

        public readonly Vector3 Size()
        {
            return SimdSize().ToOpenTK();
        }

        public readonly Vector128<float> SimdSize()
        {
            // Unfortunately Simd.ToOpenTK() adds overhead because return value is stored on stack, see if this is fixed in NET10

            return SimdMax - SimdMin;
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

        public readonly int LargestAxis()
        {
            Vector128<float> size = SimdSize();
            int axis = 0;
            if (size[0] < size[1]) axis = 1;
            if (size[axis] < size[2]) axis = 2;
            return axis;
        }

        public readonly float LargestExtent()
        {
            Vector128<float> size = SimdSize();
            return MyMath.NativeMax(size[0], MyMath.NativeMax(size[1], size[2]));
        }

        public readonly float Area()
        {
            return HalfArea() * 2.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // It's small and called in hot loop
        public readonly float HalfArea()
        {
            Vector128<float> size = SimdSize();
            float area = MyMath.HalfArea(size[0], size[1], size[2]);

            return area;
        }

        public void Transform(in Matrix4 matrix)
        {
            this = Transformed(this, matrix);
        }

        public static bool operator ==(in Box a, in Box b)
        {
            return a.SimdMin == b.SimdMin && a.SimdMax == b.SimdMax;
        }

        public static bool operator !=(in Box a, in Box b)
        {
            return !(a == b);
        }

        public static float GetOverlappingHalfArea(in Box a, in Box b)
        {
            Box shrinked = new Box(a.Min, a.Max);
            shrinked.ShrinkToFit(b);

            const float epsilon = 0.001f; // handle imprecision
            Vector3 axesOverlap = shrinked.Max - shrinked.Min;
            if (axesOverlap.X <= epsilon || axesOverlap.Y <= epsilon || axesOverlap.Z <= epsilon)
            {
                return 0.0f;
            }

            return MyMath.HalfArea(axesOverlap.X, axesOverlap.Y, axesOverlap.Z);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // It's small and tends be a size decreasing inline
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

        public static Box From(in Box a, in Box b)
        {
            Box box = new Box(a.SimdMin, a.SimdMax);
            box.GrowToFit(b);

            return box;
        }
    }
}
