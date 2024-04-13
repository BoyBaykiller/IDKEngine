using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

namespace IDKEngine.Render
{
    class TAAResolve : IDisposable
    {
        public bool IsNaiveTaa;
        public float PreferAliasingOverBlur;

        public Texture Result => (frame % 2 == 0) ? taaPing : taaPong;
        public Texture PrevResult => (frame % 2 == 0) ? taaPong : taaPing;

        private Texture taaPing;
        private Texture taaPong;
        private readonly AbstractShaderProgram taaResolveProgram;
        private int frame;
        public TAAResolve(Vector2i size, float preferAliasingOverBlurAmount = 0.25f)
        {
            taaResolveProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "TAAResolve/compute.glsl"));

            SetSize(size);

            PreferAliasingOverBlur = preferAliasingOverBlurAmount;
        }

        public void RunTAA(Texture color)
        {
            frame++;

            Result.BindToImageUnit(0, Result.TextureFormat);
            PrevResult.BindToUnit(0);
            color.BindToUnit(1);

            taaResolveProgram.Upload("PreferAliasingOverBlur", PreferAliasingOverBlur);
            taaResolveProgram.Upload("IsNaiveTaa", IsNaiveTaa);

            taaResolveProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(Vector2i size)
        {
            if (taaPing != null) taaPing.Dispose();
            taaPing = new Texture(Texture.Type.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R16G16B16A16Float);

            if (taaPong != null) taaPong.Dispose();
            taaPong = new Texture(Texture.Type.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R16G16B16A16Float);
        }

        public void Dispose()
        {
            taaPing.Dispose();
            taaPong.Dispose();
            taaResolveProgram.Dispose();
        }
    }
}
