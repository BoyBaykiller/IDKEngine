using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

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
                SetSize(PresentationResolution);
            }
        }

        public GpuSettings Settings;

        public Texture Result;
        private Texture depthTexture;
        private Texture volumetricLightingTexture;

        private readonly AbstractShaderProgram volumetricLightingProgram;
        private readonly AbstractShaderProgram upscaleProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public VolumetricLighting(Vector2i size, in GpuSettings settings, float resolutionScale = 0.6f)
        {
            volumetricLightingProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "VolumetricLight/compute.glsl"));
            upscaleProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "VolumetricLight/Upscale/compute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            _resolutionScale = resolutionScale;
            SetSize(size);

            Settings = settings;
        }

        public void Compute()
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            volumetricLightingTexture.BindToImageUnit(0, volumetricLightingTexture.TextureFormat);
            depthTexture.BindToImageUnit(1, depthTexture.TextureFormat);
            volumetricLightingProgram.Use();
            GL.DispatchCompute((volumetricLightingTexture.Width + 8 - 1) / 8, (volumetricLightingTexture.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            Result.BindToImageUnit(0, Result.TextureFormat);
            volumetricLightingTexture.BindToUnit(0);
            depthTexture.BindToUnit(1);
            upscaleProgram.Use();
            GL.DispatchCompute((PresentationResolution.X + 8 - 1) / 8, (PresentationResolution.Y + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(Vector2i size)
        {
            PresentationResolution = new Vector2i(size.X, size.Y);
            RenderResolution = (Vector2i)((Vector2)PresentationResolution * ResolutionScale);

            if (Result != null) Result.Dispose();
            Result = new Texture(Texture.Type.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(PresentationResolution.X, PresentationResolution.Y, 1, Texture.InternalFormat.R16G16B16A16Float);

            if (volumetricLightingTexture != null) volumetricLightingTexture.Dispose();
            volumetricLightingTexture = new Texture(Texture.Type.Texture2D);
            volumetricLightingTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            volumetricLightingTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            volumetricLightingTexture.ImmutableAllocate(RenderResolution.X, RenderResolution.Y, 1, Texture.InternalFormat.R16G16B16A16Float);

            if (depthTexture != null) depthTexture.Dispose();
            depthTexture = new Texture(Texture.Type.Texture2D);
            depthTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            depthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            depthTexture.ImmutableAllocate(RenderResolution.X, RenderResolution.Y, 1, Texture.InternalFormat.R32Float);
        }

        public void Dispose()
        {
            volumetricLightingTexture.Dispose();
            volumetricLightingProgram.Dispose();
            upscaleProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
