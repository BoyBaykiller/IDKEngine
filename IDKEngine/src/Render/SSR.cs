using System;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class SSR : IDisposable
    {
        public struct GpuSettings
        {
            public int SampleCount;
            public int BinarySearchCount;
            public float MaxDist;

            public static GpuSettings Default = new GpuSettings()
            {
                SampleCount = 30,
                BinarySearchCount = 8,
                MaxDist = 50.0f
            };
        }

        public GpuSettings Settings;


        public Texture Result;
        private readonly ShaderProgram shaderProgram;
        private readonly TypedBuffer<GpuSettings> bufferGpuSettings;
        public SSR(int width, int height, in GpuSettings settings)
        {
            shaderProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "SSR/compute.glsl"));

            bufferGpuSettings = new TypedBuffer<GpuSettings>();
            bufferGpuSettings.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(width, height);

            Settings = settings;
        }

        public void Compute(Texture colorTexture)
        {
            bufferGpuSettings.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            bufferGpuSettings.UploadElements(Settings);

            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            colorTexture.BindToUnit(0);

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
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
            bufferGpuSettings.Dispose();
        }
    }
}
