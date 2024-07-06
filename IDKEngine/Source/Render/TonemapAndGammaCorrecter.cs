using System;
using OpenTK.Mathematics;
using BBOpenGL;

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
            public bool IsAgXTonemaping;

            public static GpuSettings Default = new GpuSettings()
            {
                Exposure = 0.45f,
                Saturation = 1.06f,
                Linear = 0.18f,
                Peak = 1.0f,
                Compression = 0.10f,
                IsAgXTonemaping = true,
            };
        }

        public GpuSettings Settings;

        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram tonemapAndGammaCorrecterProgram;
        private readonly BBG.TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public unsafe TonemapAndGammaCorrect(Vector2i size, in GpuSettings settings)
        {
            tonemapAndGammaCorrecterProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderStage.Compute, "TonemapAndGammaCorrect/compute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.Synced, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Compute(BBG.Texture texture0 = null, BBG.Texture texture1 = null, BBG.Texture texture2 = null)
        {
            BBG.Computing.Compute("Merge Textures and do Tonemapping + Gamma Correction", () =>
            {
                gpuSettingsBuffer.BindBufferBase(BBG.Buffer.BufferTarget.Uniform, 7);
                gpuSettingsBuffer.UploadElements(Settings);

                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.BindTextureUnit(texture0, 0, texture0 != null);
                BBG.Cmd.BindTextureUnit(texture1, 1, texture1 != null);
                BBG.Cmd.BindTextureUnit(texture2, 2, texture2 != null);
                BBG.Cmd.UseShaderProgram(tonemapAndGammaCorrecterProgram);

                BBG.Computing.Dispatch((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });
        }

        public void SetSize(Vector2i size)
        {
            if (Result != null) Result.Dispose();
            Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
            Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            Result.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            Result.ImmutableAllocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R8G8B8A8Unorm);
        }
        
        public void Dispose()
        {
            Result.Dispose();
            tonemapAndGammaCorrecterProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
