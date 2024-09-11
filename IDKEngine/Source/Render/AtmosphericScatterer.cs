using System;
using BBOpenGL;

namespace IDKEngine.Render
{
    class AtmosphericScatterer : IDisposable
    {
        public record struct GpuSettings
        {
            public int ISteps;
            public int JSteps;
            public float LightIntensity;
            public float Azimuth;
            public float Elevation;

            public static GpuSettings Default = new GpuSettings()
            {
                ISteps = 40,
                JSteps = 8,
                LightIntensity = 15.0f,
                Azimuth = 0.0f,
                Elevation = 0.0f,
            };
        }

        public GpuSettings Settings;

        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        private readonly BBG.TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public unsafe AtmosphericScatterer(int size, in GpuSettings settings)
        {
            shaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "AtmosphericScattering/compute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);

            Settings = settings;

            SetSize(size);
        }

        public void Compute()
        {
            BBG.Computing.Compute("Compute Atmospheric Scattering", () =>
            {
                Settings.LightIntensity = MathF.Max(Settings.LightIntensity, 0.0f);

                gpuSettingsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 7);
                gpuSettingsBuffer.UploadElements(Settings);

                BBG.Cmd.BindImageUnit(Result, 0, 0, true);
                BBG.Cmd.UseShaderProgram(shaderProgram);

                BBG.Computing.Dispatch((Result.Width + 8 - 1) / 8, (Result.Width + 8 - 1) / 8, 6);
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
            gpuSettingsBuffer.Dispose();
            shaderProgram.Dispose();
            Result.Dispose();
        }
    }
}
