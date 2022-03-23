using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PostCombine
    {
        public readonly Texture Result;
        
        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PostCombine/compute.glsl")));
        public PostCombine(int width, int height)
        {
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Result.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba8, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
        }

        public void Compute(Texture v0, Texture v1, Texture v2, Texture v3)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba8);

            if (v0 != null) v0.BindToUnit(0);
            else Texture.UnbindFromUnit(0);

            if (v1 != null) v1.BindToUnit(1);
            else Texture.UnbindFromUnit(1);

            if (v2 != null) v2.BindToUnit(2);
            else Texture.UnbindFromUnit(2);

            if (v3 != null) v3.BindToUnit(3);
            else Texture.UnbindFromUnit(3);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 4 - 1) / 4, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }


        public void SetSize(int width, int height)
        {
            Result.MutableAllocate(width, height, 1, Result.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
        }
    }
}
