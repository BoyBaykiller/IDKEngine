using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace IDKEngine
{
    static class MyMath
    {
        public static Vector4[] GetMapedHaltonSequence_2_3(int length, int width, int height)
        {
            Vector4[] haltonSequence = new Vector4[length];
            float xScale = 1.0f / width;
            float yScale = 1.0f / height;

            for (int i = 0; i < haltonSequence.Length; i++)
            {
                float haltonValue = GetHalton(i + 1, i % 2 == 0 ? 2 : 3);
                haltonSequence[i][i % 4] = (haltonValue - 0.5f) * (i % 2 == 0 ? xScale : yScale);
            }

            return haltonSequence;
        }
        public static float GetHalton(int index, int haltonBase)
        {
            float f = 1.0f;
            float haltonValue = 0.0f;

            while (index > 0)
            {
                f /= haltonBase;
                haltonValue += f * (index % haltonBase);
                index = (int)MathF.Floor(index / (float)haltonBase);
            }

            return haltonValue;
        }

        public static void BitsInsert(ref uint mem, uint data, int offset, int bits)
        {
            mem |= GetBits(data, 0, bits) << offset;
        }

        public static uint GetBits(uint data, int offset, int bits)
        {
            return data & (((1u << bits) - 1u) << offset);
        }
    }
}
