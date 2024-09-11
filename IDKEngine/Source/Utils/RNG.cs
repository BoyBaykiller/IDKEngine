using System;
using OpenTK.Mathematics;

namespace IDKEngine.Utils
{
    public static class RNG
    {
        private static readonly Random rng = new Random();

        public static Vector3 RandomVec3()
        {
            return RandomVec3(0.0f, 1.0f);
        }

        public static Vector3 RandomVec3(float min, float max)
        {
            return new Vector3(min) + new Vector3(RandomFloat01(), RandomFloat01(), RandomFloat01()) * (max - min);
        }

        public static Vector3 RandomVec3(Vector3 min, Vector3 max)
        {
            return new Vector3(RandomFloat(min.X, max.X), RandomFloat(min.Y, max.Y), RandomFloat(min.Z, max.Z));
        }

        public static float RandomFloat01()
        {
            return rng.NextSingle();
        }

        public static int RandomInt(int min, int max)
        {
            return rng.Next(min, max);
        }

        public static float RandomFloat(float min, float max)
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

        public static Vector3 SampleHemisphere(Vector3 normal, float rnd0, float rnd1)
        {
            Vector3 dir = SampleSphere(rnd0, rnd1);
            return dir * MathF.Sign(Vector3.Dot(dir, normal));
        }

        public static Vector3 SampleHemisphere(Vector3 normal)
        {
            return SampleHemisphere(normal, RandomFloat01(), RandomFloat01());
        }
    }
}
