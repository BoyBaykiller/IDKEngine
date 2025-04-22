using System;
using OpenTK.Mathematics;

namespace IDKEngine.Utils
{
    public static class MyMath
    {
        public static int DivUp(int numerator, int divisor)
        {
            return (numerator + divisor - 1) / divisor;
        }

        public static uint DivUp(uint numerator, uint divisor)
        {
            return (numerator + divisor - 1) / divisor;
        }

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

        public static Vector3 Reflect(Vector3 incident, Vector3 normal)
        {
            return incident - 2.0f * Vector3.Dot(normal, incident) * normal;
        }

        public static Matrix3 GetTBN(Vector3 tangent, Vector3 normal)
        {
            Vector3 N = Vector3.Normalize(normal);
            Vector3 T = Vector3.Normalize(tangent);
            // Gramschmidt Process (makes sure T and N always 90 degress)
            // T = normalize(T - dot(T, N) * N);
            Vector3 B = Vector3.Normalize(Vector3.Cross(N, T));
            return new Matrix3(T, B, N);
        }

        public static Vector3 PolarToCartesian(float azimuth, float elevation, float length = 1.0f)
        {
            // https://en.wikipedia.org/wiki/Spherical_coordinate_system
            // azimuth   = phi
            // elevation = theta
            // length    = rho

            float sinTheta = MathF.Sin(elevation);
            Vector3 pos = new Vector3(sinTheta * MathF.Cos(azimuth), MathF.Cos(elevation), sinTheta * MathF.Sin(azimuth)) * length;
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

        public static float HalfArea(float sizeX, float sizeY, float sizeZ)
        {
            float x = sizeX + sizeY;
            float y = sizeZ;
            float z = sizeX * sizeY;
            float area = float.MultiplyAddEstimate(x, y, z);

            return area;
        }

        public static bool AlmostEqual(float a, float b, float epsilon)
        {
            return MathF.Abs(a - b) < epsilon;
        }

        public static float Remap(float value, float valueMin, float valueMax, float mapMin, float mapMax)
        {
            return (value - valueMin) / (valueMax - valueMin) * (mapMax - mapMin) + mapMin;
        }

        public static Vector3 Remap(Vector3 value, Vector3 valueMin, Vector3 valueMax, Vector3 mapMin, Vector3 mapMax)
        {
            Vector3 temp = (valueMax - valueMin);
            Vector3 result = (value - valueMin) / temp * (mapMax - mapMin) + mapMin;

            if (temp.X == 0.0f) result.X = 0.0f; 
            if (temp.Y == 0.0f) result.Y = 0.0f;
            if (temp.Z == 0.0f) result.Z = 0.0f;

            return result;
        }

        public static Vector3 MapToZeroOne(Vector3 value, Vector3 rangeMin, Vector3 rangeMax)
        {
            return Remap(value, rangeMin, rangeMax, new Vector3(0.0f), new Vector3(1.0f));
        }

        public static float MapToZeroOne(float value, float rangeMin, float rangeMax)
        {
            return Remap(value, rangeMin, rangeMax, 0.0f, 1.0f);
        }

        public static int NextMultiple(int num, int multiple)
        {
            return ((num / multiple) + 1) * multiple;
        }

        public static int NextPowerOfTwo(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;

            return v;
        }

        /// Source: https://developer.nvidia.com/blog/thinking-parallel-part-iii-tree-construction-gpu/

        // Expands a 10-bit integer into 30 bits
        // by inserting 2 zeros after each bit.
        private static unsafe uint ExpandBits(uint v)
        {
            unchecked
            {
                v = (v * 0x00010001u) & 0xFF0000FFu;
                v = (v * 0x00000101u) & 0x0F00F00Fu;
                v = (v * 0x00000011u) & 0xC30C30C3u;
                v = (v * 0x00000005u) & 0x49249249u;

                return v;
            }
        }

        // Calculates a 30-bit Morton code for the
        // given 3D point located within the unit cube [0,1].
        public static uint GetMortonCode(Vector3 normalizedV)
        {
            unchecked
            {
                uint x = Math.Clamp((uint)(normalizedV.X * 1024.0f), 0, 1023);
                uint y = Math.Clamp((uint)(normalizedV.Y * 1024.0f), 0, 1023);
                uint z = Math.Clamp((uint)(normalizedV.Z * 1024.0f), 0, 1023);

                uint xx = ExpandBits(x);
                uint yy = ExpandBits(y);
                uint zz = ExpandBits(z);
                uint result = xx * 4 + yy * 2 + zz;

                return result;
            }
        }

        public static float DegreesToRadians(float degrees)
        {
            const float degToRad = MathF.PI / 180.0f;
            return degrees * degToRad;
        }

        public static float RadiansToDegrees(float radians)
        {
            const float radToDeg = 180.0f / MathF.PI;
            return radians * radToDeg;
        }

        public static Matrix3x4 Matrix4x4ToTranposed3x4(in Matrix4 model)
        {
            Matrix4x3 fourByThree = new Matrix4x3(
                model.Row0.Xyz,
                model.Row1.Xyz,
                model.Row2.Xyz,
                model.Row3.Xyz
            );

            Matrix3x4 result = Matrix4x3.Transpose(fourByThree);

            return result;
        }

        public static Matrix4 Matrix3x4ToTransposed4x4(in Matrix3x4 model)
        {
            Matrix4x3 tranposed = Matrix3x4.Transpose(model);

            Matrix4 result = new Matrix4(
                new Vector4(tranposed.Row0, 0.0f),
                new Vector4(tranposed.Row1, 0.0f),
                new Vector4(tranposed.Row2, 0.0f),
                new Vector4(tranposed.Row3, 1.0f)
            );

            return result;
        }
    }
}
