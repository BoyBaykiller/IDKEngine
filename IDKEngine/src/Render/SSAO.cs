using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

namespace IDKEngine.Render
{
    class SSAO : IDisposable
    {
        public struct GpuSettings
        {
            public int SampleCount;
            public float Radius;
            public float Strength;

            public static GpuSettings Default = new GpuSettings()
            {
                SampleCount = 10,
                Radius = 0.2f,
                Strength = 1.3f
            };
        }

        public GpuSettings Settings;

        public Texture Result;
        private readonly AbstractShaderProgram shaderProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public SSAO(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "SSAO/compute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Compute()
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            Result.BindToImageUnit(0, Result.TextureFormat);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(Vector2i size)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(Texture.Type.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R8Unorm);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
