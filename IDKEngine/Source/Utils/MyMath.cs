using System;
using OpenTK.Mathematics;

namespace IDKEngine.Utils
{
    public static class MyMath
    {
        public static Vector2 GetHalton2D(int index, int baseA, int baseB)
        {
            float x = GetHalton(index + 1, baseA);
            float y = GetHalton(index + 1, baseB);

            return new Vector2(x, y);
        }

        public static float GetHalton(int index, int haltonBase)
        {
            float f = 1.0f, result = 0.0f;

            for (int currentIndex = index; currentIndex > 0;)
            {
                f /= haltonBase;
                result = result + f * (currentIndex % haltonBase);
                currentIndex = (int)MathF.Floor((float)currentIndex / haltonBase);
            }

            return result;
        }

        public static float Area(in Vector3 size)
        {
            float area = 2.0f * (size.X * size.Y + size.X * size.Z + size.Z * size.Y);
            return area;
        }

        public static float Volume(in Vector3 size)
        {
            return size.X * size.Y * size.X;
        }

        public static Vector3 PolarToCartesian(float elevation, float azimuth)
        {
            Vector3 pos = new Vector3(MathF.Sin(elevation) * MathF.Cos(azimuth), MathF.Cos(elevation), MathF.Sin(elevation) * MathF.Sin(azimuth));
            return pos;
        }

        public static Matrix4 CreatePerspectiveFieldOfViewDepthZeroToOne(float fovY, float aspect, float depthNear, float depthFar)
        {
            Matrix4 result = Matrix4.CreatePerspectiveFieldOfView(fovY, aspect, depthNear, depthFar);

            // [0, 1] depth
            result[2, 2] = depthFar / (depthNear - depthFar);
            result[3, 2] = -(depthFar * depthNear) / (depthFar - depthNear);
            return result;
        }

        public static Matrix4 CreateOrthographicOffCenterDepthZeroToOne(float left, float right, float bottom, float top, float depthNear, float depthFar)
        {
            Matrix4 result = Matrix4.CreateOrthographicOffCenter(left, right, bottom, top, depthNear, depthFar);

            // [0, 1] depth
            result[2, 2] = -1.0f / (depthFar - depthNear);
            result[3, 2] = -depthNear / (depthFar - depthNear);
            return result;
        }

        public static void GetFrustumPoints(in Matrix4 matrix, Span<Vector3> points)
        {
            for (int j = 0; j < 2; j++)
            {
                float z = j;
                Vector4 leftBottom = new Vector4(-1.0f, -1.0f, z, 1.0f) * matrix;
                Vector4 rightBottom = new Vector4(1.0f, -1.0f, z, 1.0f) * matrix;
                Vector4 leftUp = new Vector4(-1.0f, 1.0f, z, 1.0f) * matrix;
                Vector4 rightUp = new Vector4(1.0f, 1.0f, z, 1.0f) * matrix;

                leftBottom /= leftBottom.W;
                rightBottom /= rightBottom.W;
                leftUp /= leftUp.W;
                rightUp /= rightUp.W;

                points[j * 4 + 0] = leftUp.Xyz;
                points[j * 4 + 1] = rightUp.Xyz;
                points[j * 4 + 2] = rightBottom.Xyz;
                points[j * 4 + 3] = leftBottom.Xyz;
            }
        }

        public static bool AlmostEqual(float a, float b, float epsilon)
        {
            return MathF.Abs(a - b) < epsilon;
        }

        public static Vector3 MapRangeToAnOther(Vector3 value, Vector3 valueMin, Vector3 valueMax, Vector3 mapMin, Vector3 mapMax)
        {
            return (value - valueMin) / (valueMax - valueMin) * (mapMax - mapMin) + mapMin;
        }

        public static Vector3 MapToZeroOne(Vector3 value, Vector3 rangeMin, Vector3 rangeMax)
        {
            return MapRangeToAnOther(value, rangeMin, rangeMax, new Vector3(0.0f), new Vector3(1.0f));
        }

        public static int NextMultiple(int num, int multiple)
        {
            return ((num / multiple) + 1) * multiple;
        }

        // Source: https://developer.nvidia.com/blog/thinking-parallel-part-iii-tree-construction-gpu/

        // Expands a 10-bit integer into 30 bits
        // by inserting 2 zeros after each bit.
        private static uint ExpandBits(uint v)
        {
            v = unchecked((v * 0x00010001u) & 0xFF0000FFu);
            v = unchecked((v * 0x00000101u) & 0x0F00F00Fu);
            v = unchecked((v * 0x00000011u) & 0xC30C30C3u);
            v = unchecked((v * 0x00000005u) & 0x49249249u);
            return v;
        }

        // Calculates a 30-bit Morton code for the
        // given 3D point located within the unit cube [0,1].
        public static uint Morton3D(in Vector3 value)
        {
            uint x = Math.Clamp((uint)(value.X * 1024.0f), 0, 1023);
            uint y = Math.Clamp((uint)(value.Y * 1024.0f), 0, 1023);
            uint z = Math.Clamp((uint)(value.Z * 1024.0f), 0, 1023);

            uint xx = ExpandBits(x);
            uint yy = ExpandBits(y);
            uint zz = ExpandBits(z);
            uint result = xx * 4 + yy * 2 + zz;

            return result;
        }
    }
}
