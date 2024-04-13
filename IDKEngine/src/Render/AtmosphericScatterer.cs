using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;
using IDKEngine.OpenGL;

namespace IDKEngine.Render
{
    class AtmosphericScatterer : IDisposable
    {
        private int _iSteps;
        public int ISteps
        {
            set
            {
                _iSteps = value;
                shaderProgram.Upload("ISteps", ISteps);
            }

            get => _iSteps;
        }

        private int _jSteps;
        public int JSteps
        {
            set
            {
                _jSteps = value;
                shaderProgram.Upload("JSteps", JSteps);
            }

            get => _jSteps;
        }

        private float _elevation;
        public float Elevation
        {
            set
            {
                _elevation = value;

                Vector3 pos = MyMath.PolarToCartesian(Elevation, Azimuth);
                shaderProgram.Upload("LightPos", pos);
            }

            get => _elevation;
        }

        private float _azimuth;
        public float Azimuth
        {
            set
            {
                _azimuth = value;

                Vector3 pos = MyMath.PolarToCartesian(Elevation, Azimuth);
                shaderProgram.Upload("LightPos", pos);
            }

            get => _azimuth;
        }

        private float _lightIntensity;
        public float LightIntensity
        {
            set
            {
                _lightIntensity = Math.Max(value, 0.0f);
                shaderProgram.Upload("LightIntensity", LightIntensity);
            }

            get => _lightIntensity;
        }

        public Texture Result;
        private readonly AbstractShaderProgram shaderProgram;
        public AtmosphericScatterer(int size)
        {
            shaderProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "AtmosphericScattering/compute.glsl"));

            Matrix4[] invViewsAndInvprojecion = new Matrix4[]
            {
                Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // PositiveX
                Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // NegativeX
               
                Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)).Inverted(), // PositiveY
                Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)).Inverted(), // NegativeY

                Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // PositiveZ
                Camera.GenerateViewMatrix(Vector3.Zero, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // NegativeZ
            };

            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90.0f), 1.0f, 69.0f, 420.0f).Inverted();
            shaderProgram.Upload("InvProjection", projection);
            shaderProgram.Upload("InvViews[0]", invViewsAndInvprojecion[0], invViewsAndInvprojecion.Length);

            SetSize(size);

            Elevation = 0.0f;
            Azimuth = 0.0f;
            ISteps = 40;
            JSteps = 8;
            LightIntensity = 15.0f;
        }

        public void Compute()
        {
            Result.BindToImageUnit(0, Result.TextureFormat, 0, true);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Width + 8 - 1) / 8, 6);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int size)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(Texture.Type.Cubemap);
            Result.ImmutableAllocate(size, size, 1, Texture.InternalFormat.R32G32B32A32Float);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
        }
    }
}
