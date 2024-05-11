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

        public static ulong Split(ulong x, int log_bits)
        {
            int bit_count = 1 << log_bits;
            ulong mask = ulong.MaxValue >> (bit_count / 2);
            x &= mask;
            for (int i = log_bits - 1, n = 1 << i; i > 0; --i, n >>= 1)
            {
                mask = (mask | (mask << n)) & ~(mask << (n / 2));
                x = (x | (x << n)) & mask;
            }
            return x;
        }

        public static ulong Encode(ulong x, ulong y, ulong z, int log_bits)
        {
            return Split(x, log_bits) | (Split(y, log_bits) << 1) | (Split(z, log_bits) << 2);
        }

        public static Vector3 Encode(Vector3 a)
        {
            return new Vector3(0.0f);
        }
    }
}
