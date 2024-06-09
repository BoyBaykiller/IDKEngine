using System;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using StbImageWriteSharp;
using BBLogger;
using BBOpenGL;

namespace IDKEngine.Utils
{
    static class Helper
    {
        public static void GLDebugCallback(
            BBG.Debugging.DebugSource source,
            BBG.Debugging.DebugType type,
            BBG.Debugging.DebugSeverity severity,
            uint messageID,
            string message)
        {
            const bool FILTER_UNWANTED = true;

            switch (severity)
            {
                case BBG.Debugging.DebugSeverity.Low:
                    Logger.Log(Logger.LogLevel.Info, message);
                    break;

                case BBG.Debugging.DebugSeverity.Medium:
                    if (FILTER_UNWANTED && messageID == 0) return; // Shader compile warning, Intel
                    if (FILTER_UNWANTED && messageID == 2) return; // using glNamedBufferSubData(buffer 35, offset 0, size 1668) to update a GL_STATIC_DRAW buffer, AMD radeonsi
                    // if (FILTER_UNWANTED && messageID == 131186) return; // Buffer object is being copied/moved from VIDEO memory to HOST memory, NVIDIA
                    if (FILTER_UNWANTED && messageID == 131154) return; // Pixel-path performance warning: Pixel transfer is synchronized with 3D rendering, NVIDIA

                    Logger.Log(Logger.LogLevel.Warn, message);
                    break;

                case BBG.Debugging.DebugSeverity.High:
                    if (FILTER_UNWANTED && messageID == 0) return; // Shader compile error, Intel
                    if (FILTER_UNWANTED && messageID == 2000) return; // Shader compile error, AMD
                    if (FILTER_UNWANTED && messageID == 2001) return; // Program link error, AMD

                    Logger.Log(Logger.LogLevel.Error, message);
                    break;

                case BBG.Debugging.DebugSeverity.Notification:
                    if (FILTER_UNWANTED && messageID == 131185) return; // Buffer detailed info, NVIDIA

                    Logger.Log(Logger.LogLevel.Info, message);
                    break;

                case BBG.Debugging.DebugSeverity.DontCare:
                default:
                    break;
            }
        }

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

        public static string ToOnOff(this bool val)
        {
            return val ? "On" : "Off";
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

        public static unsafe void TextureToDiskJpg(BBG.Texture texture, string path, int quality = 100, bool flipVertically = true)
        {
            int nChannels = 3;

            byte* pixels = Memory.Malloc<byte>(texture.Width * texture.Height * nChannels);
            texture.GetImageData(BBG.Texture.NumChannelsToPixelFormat(nChannels), BBG.Texture.PixelType.UByte, pixels, texture.Width * texture.Height * nChannels * sizeof(byte));

            using FileStream fileStream = File.OpenWrite($"{path}.jpg");
            ImageWriter imageWriter = new ImageWriter();

            StbImageWrite.stbi_flip_vertically_on_write(flipVertically ? 1 : 0);
            imageWriter.WriteJpg(pixels, texture.Width, texture.Height, ColorComponents.RedGreenBlue, fileStream, quality);

            Memory.Free(pixels);
        }
    }
}