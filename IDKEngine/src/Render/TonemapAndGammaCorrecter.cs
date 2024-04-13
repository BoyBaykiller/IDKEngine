using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

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
        private readonly AbstractShaderProgram tonemapAndGammaCorrecterProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public TonemapAndGammaCorrect(Vector2i size, in GpuSettings settings)
        {
            tonemapAndGammaCorrecterProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "TonemapAndGammaCorrect/compute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Combine(Texture texture0 = null, Texture texture1 = null, Texture texture2 = null)
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            Result.BindToImageUnit(0, Result.TextureFormat);

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

        public void SetSize(Vector2i size)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(Texture.Type.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R8G8B8A8Unorm);
        }
        
        public void Dispose()
        {
            Result.Dispose();
            tonemapAndGammaCorrecterProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
