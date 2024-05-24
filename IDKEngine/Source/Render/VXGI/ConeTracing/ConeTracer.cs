using System;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.Render
{
    class ConeTracer : IDisposable
    {
        public struct GpuSettings
        {
            public int MaxSamples;
            public float StepMultiplier;
            public float GIBoost;
            public float GISkyBoxBoost;
            public float NormalRayOffset;
            public bool IsTemporalAccumulation;

            public static GpuSettings Default = new GpuSettings()
            {
                NormalRayOffset = 1.0f,
                MaxSamples = 4,
                GIBoost = 1.3f,
                GISkyBoxBoost = 1.0f / 1.3f,
                StepMultiplier = 0.16f,
                IsTemporalAccumulation = true
            };
        }

        public GpuSettings Settings;

        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        private readonly BBG.TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public unsafe ConeTracer(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderStage.Compute, "VXGI/ConeTracing/compute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Compute(BBG.Texture voxelsAlbedo)
        {
            BBG.Computing.Compute("Cone Trace voxel texture", () =>
            {
                gpuSettingsBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 7);
                gpuSettingsBuffer.UploadElements(Settings);

                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.BindTextureUnit(voxelsAlbedo, 0);
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
            Result.ImmutableAllocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
