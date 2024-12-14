using System;
using OpenTK.Mathematics;

namespace IDKEngine.Utils
{
    public static class RNG
    {
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
        public static float RandomFloat(float min, float max)
        {
            return min + RandomFloat01() * (max - min);
        }

        public static float RandomFloat01()
        {
            return Random.Shared.NextSingle();
        }

        public static int RandomInt(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }
    }
}
