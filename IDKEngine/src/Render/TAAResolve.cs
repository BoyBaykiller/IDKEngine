using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class TAAResolve : IDisposable
    {
        private bool _isNaiveTaa;
        public bool IsNaiveTaa
        {
            get => _isNaiveTaa;

            set
            {
                _isNaiveTaa = value;
                taaResolveProgram.Upload("IsNaiveTaa", _isNaiveTaa);
            }
        }

        private float _preferAliasingOverBlur;
        public float PreferAliasingOverBlur
        {
            get => _preferAliasingOverBlur;

            set
            {
                _preferAliasingOverBlur = value;
                taaResolveProgram.Upload("PreferAliasingOverBlur", _preferAliasingOverBlur);
            }
        }

        public Texture Result => (frame % 2 == 0) ? taaPing : taaPong;
        public Texture PrevResult => (frame % 2 == 0) ? taaPong : taaPing;

        private Texture taaPing;
        private Texture taaPong;
        private readonly ShaderProgram taaResolveProgram;
        private int frame;
        public TAAResolve(int width, int height, float favourAliasingOverBlurAmount = 0.25f)
        {
            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TAAResolve/compute.glsl")));

            SetSize(width, height);

            PreferAliasingOverBlur = favourAliasingOverBlurAmount;
        }

        public void RunTAA(Texture color)
        {
            frame++;

            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            PrevResult.BindToUnit(0);
            color.BindToUnit(1);
            
            taaResolveProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            if (taaPing != null) taaPing.Dispose();
            taaPing = new Texture(TextureTarget2d.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            if (taaPong != null) taaPong.Dispose();
            taaPong = new Texture(TextureTarget2d.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);
        }

        public void Dispose()
        {
            taaPing.Dispose();
            taaPong.Dispose();
            taaResolveProgram.Dispose();
        }
    }
}
