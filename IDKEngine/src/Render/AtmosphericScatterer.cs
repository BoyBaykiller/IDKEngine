using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class AtmosphericScatterer
    {
        private int _iSteps;
        public int ISteps
        {
            set
            {
                _iSteps = value;
                shaderProgram.Upload("ISteps", _iSteps);
            }

            get => _iSteps;
        }

        private int _jSteps;
        public int JSteps
        {
            set
            {
                _jSteps = value;
                shaderProgram.Upload("JSteps", _jSteps);
            }

            get => _jSteps;
        }

        private float _time;
        public float Time
        {
            set
            {
                _time = value;
                shaderProgram.Upload("LightPos", new Vector3(0.0f, MathF.Sin(MathHelper.DegreesToRadians(_time * 360.0f)), MathF.Cos(MathHelper.DegreesToRadians(_time * 360.0f))) * 149600000e3f);
            }

            get => _time;
        }

        private float _lightIntensity;
        public float LightIntensity
        {
            set
            {
                _lightIntensity = Math.Max(value, 0.0f);
                shaderProgram.Upload("LightIntensity", _lightIntensity);
            }

            get => _lightIntensity;
        }

        public Texture Result;
        private readonly ShaderProgram shaderProgram;
        public AtmosphericScatterer(int size)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/AtmosphericScattering/compute.glsl")));

            SetSize(size);

            Time = 0.05f;
            ISteps = 40;
            JSteps = 8;
            LightIntensity = 15.0f;
        }

        public void Compute()
        {
            Result.BindToImageUnit(0, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Width + 8 - 1) / 8, 6);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int size)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.TextureCubeMap);
            Result.ImmutableAllocate(size, size, 1, SizedInternalFormat.Rgba32f);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
        }
    }
}
