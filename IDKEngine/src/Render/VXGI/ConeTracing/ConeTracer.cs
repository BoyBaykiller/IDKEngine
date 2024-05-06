using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

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
                GIBoost = 2.0f,
                GISkyBoxBoost = 1.0f / 2.0f,
                StepMultiplier = 0.16f,
                IsTemporalAccumulation = true
            };
        }

        public GpuSettings Settings;

        public Texture Result;
        private readonly AbstractShaderProgram shaderProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public ConeTracer(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "VXGI/ConeTracing/compute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Compute(Texture voxelsAlbedo)
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            Result.BindToImageUnit(0, Result.TextureFormat);
            voxelsAlbedo.BindToUnit(0);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(Vector2i size)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(Texture.Type.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R16G16B16A16Float);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
