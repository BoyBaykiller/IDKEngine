using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class VolumetricLighting : IDisposable
    {
        private int _samples;
        public int Samples
        {
            get => _samples;

            set
            {
                _samples = value;
                volumetricLightingProgram.Upload("Samples", _samples);
            }
        }

        private float _scattering;
        public float Scattering
        {
            get => _scattering;

            set
            {
                _scattering = value;
                volumetricLightingProgram.Upload("Scattering", _scattering);
            }
        }

        private float _maxDist;
        public float MaxDist
        {
            get => _maxDist;

            set
            {
                _maxDist = value;
                volumetricLightingProgram.Upload("MaxDist", _maxDist);
            }
        }

        private Vector3 _absorbance;
        public Vector3 Absorbance
        {
            get => _absorbance;

            set
            {
                _absorbance = value;
                volumetricLightingProgram.Upload("Absorbance", _absorbance);
            }
        }

        private float _strength;
        public float Strength
        {
            get => _strength;

            set
            {
                _strength = value;
                volumetricLightingProgram.Upload("Strength", _strength);
            }
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

        public Texture Result;
        private Texture depthTexture;
        private Texture volumetricLightingTexture;

        private readonly ShaderProgram volumetricLightingProgram;
        private readonly ShaderProgram upscaleProgram;
        public VolumetricLighting(int width, int height, int samples, float scattering, float maxDist, float strength, Vector3 absorbance, float resolutionScale = 0.5f)
        {
            volumetricLightingProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, System.IO.File.ReadAllText("res/shaders/VolumetricLight/compute.glsl")));
            upscaleProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, System.IO.File.ReadAllText("res/shaders/VolumetricLight/Upscale/compute.glsl")));

            _resolutionScale = resolutionScale;
            SetSize(width, height);

            Samples = samples;
            Scattering = scattering;
            MaxDist = maxDist;
            Strength = strength;
            Absorbance = absorbance;
        }

        public void Compute()
        {
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
        }
    }
}
