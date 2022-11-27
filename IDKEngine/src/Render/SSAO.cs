using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class SSAO : IDisposable
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

        private float _radius;
        public float Radius
        {
            get => _radius;

            set
            {
                _radius = value;
                shaderProgram.Upload("Radius", _radius);
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
        public SSAO(int width, int height, int samples, float radius, float strength)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/SSAO/compute.glsl")));

            SetSize(width, height);

            Samples = samples;
            Radius = radius;
            Strength = strength;
        }

        public void Compute(Texture depth, Texture normal)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            depth.BindToUnit(0);
            normal.BindToUnit(1);

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
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.R8);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
        }
    }
}
