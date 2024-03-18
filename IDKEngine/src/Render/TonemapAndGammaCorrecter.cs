using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class TonemapAndGammaCorrect : IDisposable
    {
        public struct GpuSettings
        {
            public float Exposure;
            public float Saturation;
            public float Linear;
            public float Peak;
            public float Compression;

            public static GpuSettings Default = new GpuSettings()
            {
                Exposure = 0.45f,
                Saturation = 1.06f,
                Linear = 0.18f,
                Peak = 1.0f,
                Compression = 0.10f
            };
        }

        public GpuSettings Settings;

        public Texture Result;
        private readonly ShaderProgram tonemapAndGammaCorrecterProgram;
        private readonly TypedBuffer<GpuSettings> bufferGpuSettings;
        public TonemapAndGammaCorrect(int width, int height, in GpuSettings settings)
        {
            tonemapAndGammaCorrecterProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "TonemapAndGammaCorrect/compute.glsl"));

            bufferGpuSettings = new TypedBuffer<GpuSettings>();
            bufferGpuSettings.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(width, height);

            Settings = settings;
        }

        public void Combine(Texture texture0 = null, Texture texture1 = null, Texture texture2 = null)
        {
            bufferGpuSettings.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            bufferGpuSettings.UploadElements(Settings);

            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);

            if (texture0 != null) texture0.BindToUnit(0);
            else Texture.UnbindFromUnit(0);

            if (texture1 != null) texture1.BindToUnit(1);
            else Texture.UnbindFromUnit(1);

            if (texture2 != null) texture2.BindToUnit(2);
            else Texture.UnbindFromUnit(2);

            tonemapAndGammaCorrecterProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba8);
        }
        
        public void Dispose()
        {
            Result.Dispose();
            tonemapAndGammaCorrecterProgram.Dispose();
            bufferGpuSettings.Dispose();
        }
    }
}
