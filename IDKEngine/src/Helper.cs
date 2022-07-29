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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    static class Helper
    {
        public static readonly double APIVersion = Convert.ToDouble($"{GL.GetInteger(GetPName.MajorVersion)}{GL.GetInteger(GetPName.MinorVersion)}") / 10.0;

        private static HashSet<string> GetExtensions()
        {
            HashSet<string> extensions = new HashSet<string>(GL.GetInteger(GetPName.NumExtensions));
            for (int i = 0; i < GL.GetInteger(GetPName.NumExtensions); i++)
                extensions.Add(GL.GetString(StringNameIndexed.Extensions, i));
            
            return extensions;
        }

        private static readonly HashSet<string> glExtensions = GetExtensions();


        /// <summary>
        /// </summary>
        /// <param name="extension">The extension to check against. Examples: GL_ARB_bindless_texture or WGL_EXT_swap_control</param>
        /// <returns>True if the extension is available</returns>
        public static bool IsExtensionsAvailable(string extension)
        {
            return glExtensions.Contains(extension);
        }

        /// <summary>
        /// </summary>
        /// <param name="extension">Extension to check against. Examples: GL_ARB_direct_state_access or GL_ARB_compute_shader</param>
        /// <param name="first">API version the extension became part of the core profile</param>
        /// <returns>True if this GL version >=<paramref name="first"/> or the extension is otherwise available</returns>
        public static bool IsCoreExtensionAvailable(string extension, double first)
        {
            return (APIVersion >= first) || IsExtensionsAvailable(extension);
        }

        public static DebugProc DebugCallback = Debug;
        private static void Debug(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam)
        {
            // Filter shader compile error and "... will use bla bla ..."
            if (id != 2000 && id != 131185)
            {
                Console.WriteLine($"\nType: {type},\nSeverity: {severity},\nMessage: {Marshal.PtrToStringAnsi(message, length - 1)}");
                if (severity == DebugSeverity.DebugSeverityHigh)
                {
                    Console.WriteLine($"Critical error detected, press enter to continue");
                    Console.ReadLine();
                }
                Console.WriteLine();
            }
        }

        public static unsafe void ParallelLoadCubemap(Texture texture, string[] paths, SizedInternalFormat sizedInternalFormat)
        {
            if (texture.Target != TextureTarget.TextureCubeMap)
                throw new ArgumentException($"texture must be {TextureTarget.TextureCubeMap}");

            if (paths.Length != 6)
                throw new ArgumentException($"Number of images must be equal to six");

            if (!paths.All(p => File.Exists(p)))
                throw new FileNotFoundException($"At least on of the specified paths is invalid");

            Image<Rgba32>[] images = new Image<Rgba32>[6];
            Parallel.For(0, 6, i =>
            {
                images[i] = Image.Load<Rgba32>(paths[i]);
            });

            if (!images.All(i => i.Width == i.Height && i.Width == images[0].Width))
                throw new ArgumentException($"Cubemap images must be squares and every texture must be of the same size");

            int size = images[0].Width;
            texture.ImmutableAllocate(size, size, 1, sizedInternalFormat);
            for (int i = 0; i < 6; i++)
            {
                fixed (void* ptr = images[i].GetPixelRowSpan(0))
                {
                    texture.SubTexture3D(size, size, 1, PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ptr, 0, 0, 0, i);
                    images[i].Dispose();
                }
            }
        }

        public static unsafe T* Malloc<T>(int count = 1) where T : unmanaged
        {
            return (T*)Marshal.AllocHGlobal(sizeof(T) * count);
        }

        public static unsafe void MemSet(void* ptr, byte value, uint byteCount)
        {
            Unsafe.InitBlock(ptr, value, byteCount);
        }

        public static unsafe void MemCpy(void* src, void* dest, int len)
        {
            // I don't like having to specify a destination size so yeah
            System.Buffer.MemoryCopy(src, dest, long.MaxValue, len);
        }

        public static uint PackR10G10B10(Vector3 v)
        {
            const int BITS = 10;
            const uint MAX_NUM = (1u << BITS) - 1;

            uint cX = (uint)MathF.Round(v.X * MAX_NUM);
            uint cY = (uint)MathF.Round(v.Y * MAX_NUM);
            uint cZ = (uint)MathF.Round(v.Z * MAX_NUM);

            uint packed = (cX << (BITS * 0)) | (cY << (BITS * 1)) | (cZ << (BITS * 2));

            return packed;
        }

        public static Vector3 UnpackR10G10B10(uint v)
        {
            const int BITS = 10;
            const uint MAX_NUM = (1u << BITS) - 1;

            uint x = (v >> (BITS * 0)) & MAX_NUM;
            uint y = (v >> (BITS * 1)) & MAX_NUM;
            uint z = (v >> (BITS * 2)) & MAX_NUM;

            float cX = x * (1.0f / MAX_NUM);
            float cY = y * (1.0f / MAX_NUM);
            float cZ = z * (1.0f / MAX_NUM);

            return new Vector3(cX, cY, cZ);
        }

        public static void Swap<T>(ref T first, ref T other) where T : struct
        {
            T temp = first;
            first = other;
            other = temp;
        }

        public delegate void FuncRunParallel(int i);
        public static Thread InParallel(int start, int endExclusive, FuncRunParallel func)
        {
            Thread thread = new Thread(() =>
            {
                Parallel.For(0, endExclusive, i =>
                {
                    func(start + i);
                });
            });
            thread.Start();
            return thread;
        }

        private static readonly Random rng = new Random();
        public static Vector3 RandomVec3(float min, float max)
        {
            return new Vector3(min) + new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()) * (max - min);
        }

        public static Vector3 RandomVec3(Vector3 min, Vector3 max)
        {
            return min + new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()) * (max - min);
        }

        public static float RandomFloat(float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }
    }
}