using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class VolumetricLighting : IDisposable
    {
        public struct GpuSettings
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
                SetSize(PresentationResolution.X, PresentationResolution.Y);
            }
        }

        public GpuSettings Settings;

        public Texture Result;
        private Texture depthTexture;
        private Texture volumetricLightingTexture;

        private readonly ShaderProgram volumetricLightingProgram;
        private readonly ShaderProgram upscaleProgram;
        private readonly TypedBuffer<GpuSettings> bufferGpuSettings;
        public VolumetricLighting(int width, int height, in GpuSettings settings, float resolutionScale = 0.5f)
        {
            volumetricLightingProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "VolumetricLight/compute.glsl"));
            upscaleProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "VolumetricLight/Upscale/compute.glsl"));

            bufferGpuSettings = new TypedBuffer<GpuSettings>();
            bufferGpuSettings.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            _resolutionScale = resolutionScale;
            SetSize(width, height);

            Settings = settings;
        }

        public void Compute()
        {
            bufferGpuSettings.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            bufferGpuSettings.UploadElements(Settings);

            volumetricLightingTexture.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, volumetricLightingTexture.SizedInternalFormat);
            depthTexture.BindToImageUnit(1, 0, false, 0, TextureAccess.WriteOnly, depthTexture.SizedInternalFormat);
            volumetricLightingProgram.Use();
            GL.DispatchCompute((volumetricLightingTexture.Width + 8 - 1) / 8, (volumetricLightingTexture.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            volumetricLightingTexture.BindToUnit(0);
            depthTexture.BindToUnit(1);
            upscaleProgram.Use();
            GL.DispatchCompute((PresentationResolution.X + 8 - 1) / 8, (PresentationResolution.Y + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            PresentationResolution = new Vector2i(width, height);
            RenderResolution = (Vector2i)((Vector2)PresentationResolution * ResolutionScale);

            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(PresentationResolution.X, PresentationResolution.Y, 1, SizedInternalFormat.Rgba16f);

            if (volumetricLightingTexture != null) volumetricLightingTexture.Dispose();
            volumetricLightingTexture = new Texture(TextureTarget2d.Texture2D);
            volumetricLightingTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            volumetricLightingTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            volumetricLightingTexture.ImmutableAllocate(RenderResolution.X, RenderResolution.Y, 1, SizedInternalFormat.Rgba16f);

            if (depthTexture != null) depthTexture.Dispose();
            depthTexture = new Texture(TextureTarget2d.Texture2D);
            depthTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            depthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            depthTexture.ImmutableAllocate(RenderResolution.X, RenderResolution.Y, 1, SizedInternalFormat.R32f);
        }

        public void Dispose()
        {
            volumetricLightingTexture.Dispose();
            volumetricLightingProgram.Dispose();
            upscaleProgram.Dispose();
            bufferGpuSettings.Dispose();
        }
    }
}
