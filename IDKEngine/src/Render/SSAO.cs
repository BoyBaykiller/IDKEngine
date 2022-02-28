using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class SSAO
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

        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/SSAO/compute.glsl")));

        public readonly Texture Result;
        public SSAO(int width, int height, int samples, float radius, float strength)
        {
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.MutableAllocate(width, height, 1, PixelInternalFormat.R8, (System.IntPtr)0, PixelFormat.Red, PixelType.Float);

            Samples = samples;
            Radius = radius;
            Strength = strength;
        }

        public void Compute(Texture depth, Texture normal)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R8);
            depth.BindToUnit(0);
            normal.BindToUnit(1);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 4 - 1) / 4, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            Result.MutableAllocate(width, height, 1, Result.PixelInternalFormat, (System.IntPtr)0, PixelFormat.Red, PixelType.Float);
        }
    }
}
