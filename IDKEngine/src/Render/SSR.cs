using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

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
        private readonly AbstractShaderProgram shaderProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public SSR(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "SSR/compute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Compute(Texture colorTexture)
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            Result.BindToImageUnit(0, Result.TextureFormat);
            colorTexture.BindToUnit(0);

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
