using System;
using OpenTK.Mathematics;

namespace IDKEngine
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

        public static float Area(Vector3 size)
        {
            if (size.X < 0.0f || size.Y < 0.0f || size.Z < 0.0f) return 0.0f;

            float area = 2.0f * (size.X * size.Y + size.X * size.Z + size.Z * size.Y);
            return area;
        }

        public static float Volume(Vector3 extends)
        {
            return extends.X * extends.Y * extends.X;
        }

        public static Vector3 Lerp(Vector3 x, Vector3 y, float a)
        {
            float xC = MathHelper.Lerp(x.X, y.X, a);
            float yC = MathHelper.Lerp(x.Y, y.Y, a);
            float zC = MathHelper.Lerp(x.Z, y.Z, a);

            return new Vector3(xC, yC, zC);
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
    }
}
