using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    unsafe class TonemapAndGammaCorrect : IDisposable
    {
        private bool _isDithering;
        public bool IsDithering
        {
            get => _isDithering;

            set
            {
                _isDithering = value;
                tonemapAndGammaCorrecterProgram.Upload("IsDithering", _isDithering);
            }
        }

        private float _gamma;
        public float Gamma
        {
            get => _gamma;

            set
            {
                _gamma = value;
                tonemapAndGammaCorrecterProgram.Upload("Gamma", _gamma);
            }
        }

        public Texture Result;
        private readonly ShaderProgram tonemapAndGammaCorrecterProgram;
        public TonemapAndGammaCorrect(int width, int height, float gamma = 2.2f)
        {
            tonemapAndGammaCorrecterProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TonemapAndGammaCorrect/compute.glsl")));

            SetSize(width, height);
            
            IsDithering = true;
            Gamma = gamma;
        }

        public void Combine(Texture texture0 = null, Texture texture1 = null, Texture texture2 = null)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);

            if (texture0 != null) texture0.BindToUnit(0);
            else Texture.UnbindFromUnit(0);

            if (texture1 != null) texture1.BindToUnit(1);
            else Texture.UnbindFromUnit(1);

            if (texture2 != null) texture2.BindToUnit(2);
            else Texture.UnbindFromUnit(2);

            tonemapAndGammaCorrecterProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba8);
        }
        
        public void Dispose()
        {
            Result.Dispose();
            tonemapAndGammaCorrecterProgram.Dispose();
        }
    }
}
