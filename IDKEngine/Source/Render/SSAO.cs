using System;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.Render
{
    class SSAO : IDisposable
    {
        public record struct GpuSettings
        {
            public int SampleCount = 10;
            public float Radius = 0.2f;
            public float Strength = 1.3f;

            public GpuSettings()
            {
            }
        }

        public GpuSettings Settings;

        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        public SSAO(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "SSAO/compute.glsl"));

            SetSize(size);

            Settings = settings;
        }

        public void Compute()
        {
            BBG.Computing.Compute("Compute SSAO", () =>
            {
                BBG.Cmd.SetUniforms(Settings);

                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.UseShaderProgram(shaderProgram);
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
            Result.Allocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R8Unorm);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
        }
    }
}
