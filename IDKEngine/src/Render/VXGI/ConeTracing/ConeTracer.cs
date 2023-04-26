using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ConeTracer : IDisposable
    {
        private float _normalRayOffset;
        public float NormalRayOffset
        {
            get => _normalRayOffset;

            set
            {
                _normalRayOffset = value;
                shaderProgram.Upload("NormalRayOffset", _normalRayOffset);
            }
        }

        private int _maxSamples;
        public int MaxSamples
        {
            get => _maxSamples;

            set
            {
                _maxSamples = value;
                shaderProgram.Upload("MaxSamples", _maxSamples);
            }
        }

        private float _giBoost;
        public float GIBoost
        {
            get => _giBoost;

            set
            {
                _giBoost = value;
                shaderProgram.Upload("GIBoost", _giBoost);
            }
        }

        private float _giSkyBoxBoost;
        public float GISkyBoxBoost
        {
            get => _giSkyBoxBoost;

            set
            {
                _giSkyBoxBoost = value;
                shaderProgram.Upload("GISkyBoxBoost", _giSkyBoxBoost);
            }
        }

        private float _stepMultiplier;
        public float StepMultiplier
        {
            get => _stepMultiplier;

            set
            {
                _stepMultiplier = value;
                shaderProgram.Upload("StepMultiplier", _stepMultiplier);
            }
        }

        private bool _isTemporalAccumulation;
        public bool IsTemporalAccumulation
        {
            get => _isTemporalAccumulation;

            set
            {
                _isTemporalAccumulation = value;
                shaderProgram.Upload("IsTemporalAccumulation", _isTemporalAccumulation);
            }
        }

        public Texture Result;
        private readonly ShaderProgram shaderProgram;
        public ConeTracer(int width, int height)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/VXGI/ConeTracing/compute.glsl")));

            SetSize(width, height);

            NormalRayOffset = 1.0f;
            MaxSamples = 4;
            GIBoost = 2.0f;
            GISkyBoxBoost = 1.0f / GIBoost;
            StepMultiplier = 0.16f;
            IsTemporalAccumulation = true;
        }

        public void Compute(Texture voxelsAlbedo)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            voxelsAlbedo.BindToUnit(0);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
        }
    }
}
