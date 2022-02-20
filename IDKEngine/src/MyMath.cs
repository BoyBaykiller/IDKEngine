using System;
using OpenTK;

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
                float f = 1;
                float haltonNum = 0;
                int paramCurrent = i + 1;
                int paramBase = i % 2 == 0 ? 2 : 3;
                do
                {
                    f /= paramBase;
                    haltonNum += f * (paramCurrent % paramBase);
                    paramCurrent = (int)MathF.Floor((float)paramCurrent / paramBase);
                } while (paramCurrent > 0);
                if (i % 2 == 0)
                {
                    // Map from [0; 1] to [-0.5; 0.5] * xScale
                    haltonSequence[i][i % 4] = (haltonNum - 0.5f) * xScale;
                }
                else
                {
                    // Map from [0; 1] to [-0.5; 0.5] * yScale
                    haltonSequence[i][i % 4] = (haltonNum - 0.5f) * yScale;
                }
            }

            return haltonSequence;
        }
    }
}
