using System;
using BBOpenGL;
using IDKEngine.Utils;

namespace IDKEngine.Render
{
    class AtmosphericScatterer : IDisposable
    {
        public record struct GpuSettings
        {
            public int ISteps = 40;
            public int JSteps = 8;
            public float LightIntensity = 15.0f;
            public float Azimuth = 0.0f;
            public float Elevation = 0.0f;

            public GpuSettings()
            {
            }
        }

        public GpuSettings Settings;

        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        public AtmosphericScatterer(int size, in GpuSettings settings)
        {
            shaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "AtmosphericScattering/compute.glsl"));

            Settings = settings;

            SetSize(size);
        }

        public void Compute()
        {
            BBG.Computing.Compute("Compute Atmospheric Scattering", () =>
            {
                Settings.LightIntensity = MathF.Max(Settings.LightIntensity, 0.0f);

                BBG.Cmd.SetUniforms(Settings);

                BBG.Cmd.BindImageUnit(Result, 0, 0, true);
                BBG.Cmd.UseShaderProgram(shaderProgram);

                BBG.Computing.Dispatch(MyMath.DivUp(Result.Width, 8), MyMath.DivUp(Result.Width, 8), 6);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });
        }

        public void SetSize(int size)
        {
            if (Result != null) Result.Dispose();
            Result = new BBG.Texture(BBG.Texture.Type.Cubemap);
            Result.Allocate(size, size, 1, BBG.Texture.InternalFormat.R32G32B32A32Float);
            Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        }

        public void Dispose()
        {
            shaderProgram.Dispose();
            Result.Dispose();
        }
    }
}
