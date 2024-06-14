using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.Render
{
    class AtmosphericScatterer : IDisposable
    {
        public struct GpuSettings
        {
            public Matrix4 InvProjection;
            public InvViewProjectionArray_6 InvViewArray;
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
            shaderProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderStage.Compute, "AtmosphericScattering/compute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.Synced, 1);

            Settings = settings;
            Settings.InvProjection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90.0f), 1.0f, 69.0f, 420.0f).Inverted();
            Settings.InvViewArray[0] = Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(); // PositiveX
            Settings.InvViewArray[1] = Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(); // NegativeX
            Settings.InvViewArray[2] = Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)).Inverted(); // PositiveY
            Settings.InvViewArray[3] = Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)).Inverted(); // NegativeY
            Settings.InvViewArray[4] = Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(); // PositiveZ
            Settings.InvViewArray[5] = Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(); // NegativeZ

            SetSize(size);
        }

        public void Compute()
        {
            BBG.Computing.Compute("Compute Atmospheric Scattering", () =>
            {
                Settings.LightIntensity = MathF.Max(Settings.LightIntensity, 0.0f);

                gpuSettingsBuffer.BindBufferBase(BBG.Buffer.BufferTarget.Uniform, 7);
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
            Result.ImmutableAllocate(size, size, 1, BBG.Texture.InternalFormat.R32G32B32A32Float);
            Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        }

        public void Dispose()
        {
            gpuSettingsBuffer.Dispose();
            shaderProgram.Dispose();
            Result.Dispose();
        }

        [InlineArray(6)]
        public struct InvViewProjectionArray_6
        {
            private Matrix4 _invViewProjection;
        }
    }
}
