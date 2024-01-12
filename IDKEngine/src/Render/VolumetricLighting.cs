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
                shaderProgram.Upload("Samples", _samples);
            }
        }

        private float _scattering;
        public float Scattering
        {
            get => _scattering;

            set
            {
                _scattering = value;
                shaderProgram.Upload("Scattering", _scattering);
            }
        }

        private float _maxDist;
        public float MaxDist
        {
            get => _maxDist;

            set
            {
                _maxDist = value;
                shaderProgram.Upload("MaxDist", _maxDist);
            }
        }

        private Vector3 _absorbance;
        public Vector3 Absorbance
        {
            get => _absorbance;

            set
            {
                _absorbance = value;
                shaderProgram.Upload("Absorbance", _absorbance);
            }
        }

        private float _strength;
        public float Strength
        {
            get => _strength;

            set
            {
                _strength = value;
                shaderProgram.Upload("Strength", _strength);
            }
        }

        public Texture Result;
        private readonly ShaderProgram shaderProgram;
        public VolumetricLighting(int width, int height, int samples, float scattering, float maxDist, float strength, Vector3 absorbance)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, System.IO.File.ReadAllText("res/shaders/VolumetricLight/compute.glsl")));

            SetSize(width, height);

            Samples = samples;
            Scattering = scattering;
            MaxDist = maxDist;
            Strength = strength;
            Absorbance = absorbance;
        }

        public void Compute()
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, Result.SizedInternalFormat);

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
        }
    }
}
