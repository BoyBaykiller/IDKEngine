using System;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.Render
{
    class VolumetricLighting : IDisposable
    {
        public record struct GpuSettings
        {
            public Vector3 Absorbance;
            public int SampleCount;
            public float Scattering;
            public float MaxDist;
            public float Strength;

            public static GpuSettings Default = new GpuSettings()
            {
                Absorbance = new Vector3(0.025f),
                SampleCount = 5,
                Scattering = 0.758f,
                MaxDist = 50.0f,
                Strength = 0.1f
            };
        }

        public Vector2i RenderResolution { get; private set; }
        public Vector2i PresentationResolution { get; private set; }

        private float _resolutionScale;
        public float ResolutionScale
        {
            get => _resolutionScale;

            set
            {
                _resolutionScale = value;
                SetSize(PresentationResolution);
            }
        }

        public GpuSettings Settings;

        public BBG.Texture Result;
        private BBG.Texture depthTexture;
        private BBG.Texture volumetricLightingTexture;

        private readonly BBG.AbstractShaderProgram volumetricLightingProgram;
        private readonly BBG.AbstractShaderProgram upscaleProgram;
        private readonly BBG.TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public unsafe VolumetricLighting(Vector2i size, in GpuSettings settings, float resolutionScale = 0.6f)
        {
            volumetricLightingProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VolumetricLight/compute.glsl"));
            upscaleProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VolumetricLight/Upscale/compute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.Synced, 1);

            _resolutionScale = resolutionScale;
            SetSize(size);

            Settings = settings;
        }

        public void Compute()
        {
            gpuSettingsBuffer.BindBufferBase(BBG.Buffer.BufferTarget.Uniform, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            BBG.Computing.Compute("Compute Volumetric Lighting", () =>
            {
                BBG.Cmd.BindImageUnit(volumetricLightingTexture, 0);
                BBG.Cmd.BindImageUnit(depthTexture, 1);
                BBG.Cmd.UseShaderProgram(volumetricLightingProgram);

                BBG.Computing.Dispatch((volumetricLightingTexture.Width + 8 - 1) / 8, (volumetricLightingTexture.Height + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });

            BBG.Computing.Compute("Upscale Volumetric Lighting", () =>
            {
                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.BindTextureUnit(volumetricLightingTexture, 0);
                BBG.Cmd.BindTextureUnit(depthTexture, 1);
                BBG.Cmd.UseShaderProgram(upscaleProgram);

                BBG.Computing.Dispatch((PresentationResolution.X + 8 - 1) / 8, (PresentationResolution.Y + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });
        }

        public void SetSize(Vector2i size)
        {
            PresentationResolution = new Vector2i(size.X, size.Y);
            RenderResolution = (Vector2i)((Vector2)PresentationResolution * ResolutionScale);

            if (Result != null) Result.Dispose();
            Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
            Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            Result.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            Result.ImmutableAllocate(PresentationResolution.X, PresentationResolution.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

            if (volumetricLightingTexture != null) volumetricLightingTexture.Dispose();
            volumetricLightingTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            volumetricLightingTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            volumetricLightingTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            volumetricLightingTexture.ImmutableAllocate(RenderResolution.X, RenderResolution.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

            if (depthTexture != null) depthTexture.Dispose();
            depthTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            depthTexture.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
            depthTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            depthTexture.ImmutableAllocate(RenderResolution.X, RenderResolution.Y, 1, BBG.Texture.InternalFormat.R32Float);
        }

        public void Dispose()
        {
            Result.Dispose();
            volumetricLightingTexture.Dispose();
            depthTexture.Dispose();

            volumetricLightingProgram.Dispose();
            upscaleProgram.Dispose();

            gpuSettingsBuffer.Dispose();
        }
    }
}
