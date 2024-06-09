using System;
using OpenTK.Mathematics;

namespace IDKEngine.Utils
{
    public static class RNG
    {
        private static readonly Random rng = new Random();

        public static Vector3 RandomVec3(float min, float max)
        {
            return new Vector3(min) + new Vector3(GetRandomFloat01(), GetRandomFloat01(), GetRandomFloat01()) * (max - min);
        }

        public static Vector3 RandomVec3(in Vector3 min, in Vector3 max)
        {
            return new Vector3(GetRandomFloat(min.X, max.X), GetRandomFloat(min.Y, max.Y), GetRandomFloat(min.Z, max.Z));
        }

        public static float GetRandomFloat01()
        {
            return rng.NextSingle();
        }

        public static float GetRandomFloat(float min, float max)
        {
            return min + rng.NextSingle() * (max - min);
        }

        public static Vector3 SampleSphere(float rnd0, float rnd1)
        {
            float z = rnd0 * 2.0f - 1.0f;
            float a = rnd1 * 2.0f * MathF.PI;
            float r = MathF.Sqrt(1.0f - z * z);
            float x = r * MathF.Cos(a);
            float y = r * MathF.Sin(a);

            return new Vector3(x, y, z);
        }

        public static Vector3 SampleHemisphere(in Vector3 normal, float rnd0, float rnd1)
        {
            Vector3 dir = SampleSphere(rnd0, rnd1);
            return dir * MathF.Sign(Vector3.Dot(dir, normal));
        }

        public static Vector3 SampleHemisphere(in Vector3 normal)
        {
            return SampleHemisphere(normal, GetRandomFloat01(), GetRandomFloat01());
        }
    }
}
