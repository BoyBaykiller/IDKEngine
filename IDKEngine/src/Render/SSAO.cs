using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

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
        private readonly ShaderProgram shaderProgram;
        private readonly TypedBuffer<GpuSettings> bufferGpuSettings;
        public SSAO(int width, int height, in GpuSettings settings)
        {
            shaderProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "SSAO/compute.glsl"));

            bufferGpuSettings = new TypedBuffer<GpuSettings>();
            bufferGpuSettings.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(width, height);

            Settings = settings;
        }

        public void Compute()
        {
            bufferGpuSettings.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            bufferGpuSettings.UploadElements(Settings);

            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.R8);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
            bufferGpuSettings.Dispose();
        }
    }
}
