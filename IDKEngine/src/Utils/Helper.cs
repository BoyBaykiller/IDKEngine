using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using StbImageSharp;
using StbImageWriteSharp;
using IDKEngine.OpenGL;

namespace IDKEngine.Utils
{
    static class Helper
    {
        public static readonly double APIVersion = Convert.ToDouble($"{GL.GetInteger(GetPName.MajorVersion)}{GL.GetInteger(GetPName.MinorVersion)}") / 10.0;
        public static readonly string API = GL.GetString(StringName.Version);
        public static readonly string GPU = GL.GetString(StringName.Renderer);

        public static unsafe int SizeInBytes<T>(this T[] data) where T : unmanaged
        {
            return sizeof(T) * data.Length;
        }

        public static int Sum<T>(this ReadOnlySpan<T> values, Func<T, int> func)
        {
            int sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += func(values[i]);
            }
            return sum;
        }

        public static void ArrayAdd<T>(ref T[] array, ReadOnlySpan<T> toAdd)
        {
            int prevLength = array.Length;
            Array.Resize(ref array, prevLength + toAdd.Length);
            toAdd.CopyTo(new Span<T>(array, prevLength, toAdd.Length));
        }

        public static void ArrayRemoveRange<T>(ref T[] array, int start, int count)
        {
            int end = start + count;
            Array.Copy(array, end, array, start, array.Length - end);
            Array.Resize(ref array, array.Length - count);
        }

        public static Vector4 ToOpenTK(this System.Numerics.Vector4 vector4)
        {
            return Unsafe.As<System.Numerics.Vector4, Vector4>(ref vector4);
        }

        public static System.Numerics.Vector3 ToNumerics(this Vector3 vector3)
        {
            return Unsafe.As<Vector3, System.Numerics.Vector3>(ref vector3);
        }
        public static Vector3 ToOpenTK(this System.Numerics.Vector3 vector3)
        {
            return Unsafe.As<System.Numerics.Vector3, Vector3>(ref vector3);
        }

        public static System.Numerics.Vector2 ToNumerics(this Vector2 vector2)
        {
            return Unsafe.As<Vector2, System.Numerics.Vector2>(ref vector2);
        }
        public static Vector2 ToOpenTK(this System.Numerics.Vector2 vector2)
        {
            return Unsafe.As<System.Numerics.Vector2, Vector2>(ref vector2);
        }

        public static Matrix4 ToOpenTK(this System.Numerics.Matrix4x4 matrix4x4)
        {
            return Unsafe.As<System.Numerics.Matrix4x4, Matrix4>(ref matrix4x4);
        }

        public enum DepthConvention
        {
            ZeroToOne = ClipDepthMode.ZeroToOne,
            NegativeOneToOne = ClipDepthMode.NegativeOneToOne,
        }
        public static void SetDepthConvention(DepthConvention mode)
        {
            GL.ClipControl(ClipOrigin.LowerLeft, (ClipDepthMode)mode);
        }

        private static HashSet<string> GetExtensions()
        {
            HashSet<string> extensions = new HashSet<string>(GL.GetInteger(GetPName.NumExtensions));
            for (int i = 0; i < GL.GetInteger(GetPName.NumExtensions); i++)
            {
                string extension = GL.GetString(StringNameIndexed.Extensions, i);
                extensions.Add(extension);
            }

            return extensions;
        }

        private static readonly HashSet<string> glExtensions = GetExtensions();
        public static bool IsExtensionsAvailable(string extension)
        {
            return glExtensions.Contains(extension);
        }
        public static bool IsCoreExtensionAvailable(string extension, double first)
        {
            return APIVersion >= first || IsExtensionsAvailable(extension);
        }

        public static DebugProc GLDebugCallbackFuncPtr = GLDebugCallback;
        public const uint GL_DEBUG_CALLBACK_APP_MAKER_ID = 0;
        private static void GLDebugCallback(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, nint message, nint userParam)
        {
            if (source == DebugSource.DebugSourceApplication && id == GL_DEBUG_CALLBACK_APP_MAKER_ID)
            {
                return;
            }

            string text = Marshal.PtrToStringAnsi(message, length);
            switch (severity)
            {
                case DebugSeverity.DebugSeverityLow:
                    Logger.Log(Logger.LogLevel.Info, text);
                    break;

                case DebugSeverity.DebugSeverityMedium:
                    if (id == 0) return; // Shader compile warning, Intel
                    if (id == 2) return; // using glNamedBufferSubData(buffer 35, offset 0, size 1668) to update a GL_STATIC_DRAW buffer, AMD radeonsi
                    // if (id == 131186) return; // Buffer object is being copied/moved from VIDEO memory to HOST memory, NVIDIA
                    if (id == 131154) return; // Pixel-path performance warning: Pixel transfer is synchronized with 3D rendering, NVIDIA
                    Logger.Log(Logger.LogLevel.Warn, text);
                    break;

                case DebugSeverity.DebugSeverityHigh:
                    if (id == 0) return; // Shader compile error, Intel
                    if (id == 2000) return; // Shader compile error, AMD
                    if (id == 2001) return; // Program link error, AMD

                    Logger.Log(Logger.LogLevel.Error, text);
                    break;

                case DebugSeverity.DebugSeverityNotification:
                    if (id == 131185) return; // Buffer detailed info, NVIDIA
                    Logger.Log(Logger.LogLevel.Info, text);
                    break;

                case DebugSeverity.DontCare:
                default:
                    break;
            }
        }

        public static unsafe T* Malloc<T>(int count = 1) where T : unmanaged
        {
            return (T*)NativeMemory.Alloc((nuint)(sizeof(T) * count));
        }

        public static unsafe void Free(void* ptr)
        {
            NativeMemory.Free(ptr);
        }

        public static unsafe void MemSet(void* ptr, byte value, nint byteCount)
        {
            NativeMemory.Fill(ptr, (nuint)byteCount, value);
        }

        public static unsafe void MemCpy<T1, T2>(in T1 src, ref T2 dest, nint byteCount)
            where T1 : unmanaged
            where T2 : unmanaged
        {
            fixed (void* srcPtr = &src, destPtr = &dest)
            {
                MemCpy(srcPtr, destPtr, (nuint)byteCount);
            }
        }

        public static unsafe void MemCpy<T1>(in T1 src, T1* dest, nint byteCount)
            where T1 : unmanaged
        {
            fixed (void* srcPtr = &src)
            {
                MemCpy(srcPtr, dest, (nuint)byteCount);
            }
        }

        public static unsafe void MemCpy(void* src, void* dest, nuint byteCount)
        {
            NativeMemory.Copy(src, dest, byteCount);
        }

        public static uint CompressSR11G11B10(in Vector3 data)
        {
            return CompressUR11G11B10(data * 0.5f + new Vector3(0.5f));
        }
        public static Vector3 DecompressSR11G11B10(uint data)
        {
            return DecompressUR11G11B10(data) * 2.0f - new Vector3(1.0f);
        }

        public static uint CompressUR11G11B10(in Vector3 data)
        {
            uint r = (uint)MathF.Round(data.X * ((1u << 11) - 1));
            uint g = (uint)MathF.Round(data.Y * ((1u << 11) - 1));
            uint b = (uint)MathF.Round(data.Z * ((1u << 10) - 1));

            uint compressed = b << 22 | g << 11 | r << 0;

            return compressed;
        }
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

        public static uint CompressUR8G8B8A8(in Vector4 data)
        {
            uint r = (uint)MathF.Round(data.X * ((1u << 8) - 1));
            uint g = (uint)MathF.Round(data.Y * ((1u << 8) - 1));
            uint b = (uint)MathF.Round(data.Z * ((1u << 8) - 1));
            uint a = (uint)MathF.Round(data.W * ((1u << 8) - 1));

            uint compressed = a << 24 | b << 16 | g << 8 | r << 0;

            return compressed;
        }

        public static int InterlockedMax(ref int location1, int value)
        {
            int initialValue;
            int newValue;
            do
            {
                initialValue = location1;
                newValue = Math.Max(initialValue, value);
            }
            while (Interlocked.CompareExchange(ref location1, newValue, initialValue) != initialValue);

            return initialValue;
        }

        private static readonly Random globalRng = new Random();
        public static Vector3 RandomVec3(float min, float max, Random generator = null)
        {
            Random rng = generator == null ? globalRng : generator;
            return new Vector3(min) + new Vector3(rng.NextSingle(), rng.NextSingle(), rng.NextSingle()) * (max - min);
        }

        public static Vector3 RandomVec3(in Vector3 min, in Vector3 max, Random generator = null)
        {
            Random rng = generator == null ? globalRng : generator;
            return min + new Vector3(rng.NextSingle(), rng.NextSingle(), rng.NextSingle()) * (max - min);
        }

        public static float RandomFloat(float min, float max, Random generator = null)
        {
            Random rng = generator == null ? globalRng : generator;
            return min + rng.NextSingle() * (max - min);
        }

        public static Vector3 VectorAbs(in Vector3 v)
        {
            return new Vector3(MathF.Abs(v.X), MathF.Abs(v.Y), MathF.Abs(v.Z));
        }

        public static unsafe void TextureToDiskJpg(Texture texture, string path, int quality = 100, bool flipVertically = true)
        {
            StbImageWrite.stbi_flip_vertically_on_write(flipVertically ? 1 : 0);

            byte* pixels = Malloc<byte>(texture.Width * texture.Height * 3);
            texture.GetImageData(PixelFormat.Rgb, PixelType.UnsignedByte, (nint)pixels, texture.Width * texture.Height * 3 * sizeof(byte));

            using FileStream fileStream = File.OpenWrite($"{path}.jpg");
            ImageWriter imageWriter = new ImageWriter();
            imageWriter.WriteJpg(pixels, texture.Width, texture.Height, StbImageWriteSharp.ColorComponents.RedGreenBlue, fileStream, quality);

            Free(pixels);
        }
    }
}