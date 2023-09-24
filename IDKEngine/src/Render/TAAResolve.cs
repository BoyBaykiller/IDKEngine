using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class TAAResolve : IDisposable
    {
        private bool _isTaaArtifactMitigation;
        public bool IsTaaArtifactMitigation
        {
            get => _isTaaArtifactMitigation;

            set
            {
                _isTaaArtifactMitigation = value;
                taaResolveProgram.Upload("IsTaaArtifactMitigation", _isTaaArtifactMitigation);
            }
        }

        public Texture Result => (frame % 2 == 0) ? taaPing : taaPong;
        public Texture PrevResult => (frame % 2 == 0) ? taaPong : taaPing;

        private Texture taaPing;
        private Texture taaPong;
        private readonly ShaderProgram taaResolveProgram;
        private int frame;
        public unsafe TAAResolve(int width, int height)
        {
            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TAAResolve/compute.glsl")));

            SetSize(width, height);
            IsTaaArtifactMitigation = true;
        }

        public unsafe void RunTAA(Texture color)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            PrevResult.BindToUnit(0);
            color.BindToUnit(1);
            
            taaResolveProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            frame++;
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
