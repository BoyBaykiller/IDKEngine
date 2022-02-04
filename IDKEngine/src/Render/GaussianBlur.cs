using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class GaussianBlur
    {
        public Texture Result => pong;

        private Texture ping;
        private Texture pong;

        public uint Strength = 4;

        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, System.IO.File.ReadAllText("res/shaders/Blur/compute.glsl")));
        public unsafe GaussianBlur(int width, int height)
        {
            ping = new Texture(TextureTarget2d.Texture2D);
            ping.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            ping.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            ping.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, 1);


            pong = new Texture(TextureTarget2d.Texture2D);
            pong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            pong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            pong.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, 1);
        }

        public void Compute(Texture src)
        {
            // FIX: Pseudo random deterministic black pixels on result texture...
            src.BindToUnit(0);
            ping.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

            shaderProgram.Use();
            for (int i = 0; i < 2 * Strength; i++)
            {
                shaderProgram.Upload(0, i % 2 == 0);

                GL.DispatchCompute((ping.Width + 8 - 1) / 8, (ping.Height + 4 - 1) / 4, 1);
                // Not sure if I need this here shouldnt memory access between dispatches be handled by opengl?
                //GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

                (i % 2 == 0 ? ping : pong).BindToUnit(0);
                (i % 2 == 0 ? pong : ping).BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            }
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            ping.Dispose();
            ping = new Texture(TextureTarget2d.Texture2D);
            ping.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            ping.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            ping.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, 1);


            pong.Dispose();
            pong = new Texture(TextureTarget2d.Texture2D);
            pong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            pong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            pong.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, 1);
        }
    }
}
