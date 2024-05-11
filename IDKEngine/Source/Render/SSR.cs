using System;
using OpenTK.Mathematics;
using BBOpenGL;

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


        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        private readonly BBG.TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public unsafe SSR(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderType.Compute, "SSR/compute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Compute(BBG.Texture colorTexture)
        {
            BBG.Computing.Compute("Compute SSR", () =>
            {
                gpuSettingsBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 7);
                gpuSettingsBuffer.UploadElements(Settings);

                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.BindTextureUnit(colorTexture, 0);
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
