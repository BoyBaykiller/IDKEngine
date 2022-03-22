using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Bloom
    {
        private int _lod;
        public int Lod
        {
            get => _lod;

            set
            {
                _lod = value;
                SetSize(Result.Width, Result.Height);
            }
        }


        private static readonly ShaderProgram shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Bloom/compute.glsl")));
        public Texture Result;
        public Bloom(int width, int height)
        {
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            width = (int)(width / 2.0f);
            height = (int)(height / 2.0f);
            _lod = Texture.GetMaxMipMaplevel(width, height, 1);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, _lod);
        }

        public void Compute(Texture src)
        {
            shaderProgram.Use();

            shaderProgram.Upload(0, 0);
            Vector3i size = Texture.GetMipMapLevelSize(Result.Width, Result.Height, 1, 0);
            
            src.BindToUnit(0);
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);
            Result.BindToUnit(0);

            #region Downsample
            for (int i = 1; i < Lod; i++)
            {
                size = Texture.GetMipMapLevelSize(Result.Width, Result.Height, 1, i);

                shaderProgram.Upload(0, i - 1);
                Result.BindToImageUnit(0, i, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);
            }
            #endregion


        }

        public void SetSize(int width, int height)
        {
            Result.Dispose();
            
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, Lod);
        }
    }
}
