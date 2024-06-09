using System;
using OpenTK.Mathematics;

namespace IDKEngine.Utils
{
    public static class Compression
    {
        public static Vector3 DecompressUR11G11B10(uint data)
        {
            float r = data >> 0 & (1u << 11) - 1;
            float g = data >> 11 & (1u << 11) - 1;
            float b = data >> 22 & (1u << 10) - 1;

            r *= 1.0f / ((1u << 11) - 1);
            g *= 1.0f / ((1u << 11) - 1);
            b *= 1.0f / ((1u << 10) - 1);

            return new Vector3(r, g, b);
        }
        public static uint CompressSR11G11B10(in Vector3 data)
        {
            return CompressUR11G11B10(data * 0.5f + new Vector3(0.5f));
        }

        public static uint CompressUR11G11B10(in Vector3 data)
        {
            uint r = (uint)MathF.Round(data.X * ((1u << 11) - 1));
            uint g = (uint)MathF.Round(data.Y * ((1u << 11) - 1));
            uint b = (uint)MathF.Round(data.Z * ((1u << 10) - 1));

            uint compressed = b << 22 | g << 11 | r << 0;

            return compressed;
        }

        public static uint CompressUR8G8B8A8(in Vector4 data)
        {
            uint r = (uint)MathF.Round(data.X * ((1u << 8) - 1));
            uint g = (uint)MathF.Round(data.Y * ((1u << 8) - 1));
            uint b = (uint)MathF.Round(data.Z * ((1u << 8) - 1));
            uint a = (uint)MathF.Round(data.W * ((1u << 8) - 1));

            uint compressed = a << 24 | b << 16 | g << 8 | r << 0;

            return compressed;
        }

        public static uint CompressSR8G8B8A8(in Vector4 data)
        {
            return CompressUR8G8B8A8(data * 0.5f + new Vector4(0.5f));
        }

        public static Vector2 EncodeUnitVec(Vector3 v)
        {
            Vector2 p = new Vector2(v.X, v.Y) * (1.0f / (MathF.Abs(v.X) + MathF.Abs(v.Y) + MathF.Abs(v.Z)));
            return (v.Z <= 0.0) ? ((new Vector2(1.0f) - Abs(new Vector2(p.Y, p.X))) * SignNotZero(p)) : p;
        }

        private static Vector2 SignNotZero(Vector2 v)
        {
            return new Vector2((v.X >= 0.0f) ? +1.0f : -1.0f, (v.Y >= 0.0f) ? +1.0f : -1.0f);
        }
        
        private static Vector2 Abs(in Vector2 v)
        {
            return new Vector2(MathF.Abs(v.X), MathF.Abs(v.Y));
        }
    }
}
