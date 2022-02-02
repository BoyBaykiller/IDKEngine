using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    class BilateralBlurer
    {
        public readonly Texture Result;
        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, System.IO.File.ReadAllText("res/shaders/Blur/bilateral.glsl")));
        public BilateralBlurer(int width, int height)
        {
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba16f);
        }

        public void Blur(Texture src)
        {
            shaderProgram.Use();
            src.BindToUnit(0);
            Result.BindToImageUnit(1, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 4 - 1) / 4, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            Result.MutableAllocate(width, height, 1, Result.PixelInternalFormat);
        }
    }
}
