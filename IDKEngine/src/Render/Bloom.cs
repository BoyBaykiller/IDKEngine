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
                SetSize(downscaleTexture.Width, downscaleTexture.Height);
            }
        }

        private enum Stage : int
        {
            FilterDownsample = 0,
            Downsample = 1,
            Upsample = 2,
        }

        public Texture Result => upsampleTexture;

        private static readonly ShaderProgram shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Bloom/compute.glsl")));
        private Texture downscaleTexture;
        private Texture upsampleTexture;
        public Bloom(int width, int height)
        {
            width = (int)(width / 2.0f);
            height = (int)(height / 2.0f);
            _lod = Texture.GetMaxMipMaplevel(width, height, 1) - 2;

            downscaleTexture = new Texture(TextureTarget2d.Texture2D);
            downscaleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            downscaleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            downscaleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, _lod);

            upsampleTexture = new Texture(TextureTarget2d.Texture2D);
            upsampleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            upsampleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upsampleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, _lod);
        }

        public void Compute(Texture src)
        {
            shaderProgram.Use();

            #region FilterDownsample
            src.BindToUnit(0);
            shaderProgram.Upload(1, (int)Stage.FilterDownsample);

            Vector3i size = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, 0);
            shaderProgram.Upload(0, 0);
            downscaleTexture.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);
            GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);
            #endregion

            downscaleTexture.BindToUnit(0);

            #region Downsample
            shaderProgram.Upload(1, (int)Stage.Downsample);
            for (int i = 1; i < Lod; i++)
            {
                size = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, i);

                shaderProgram.Upload(0, i - 1);
                downscaleTexture.BindToImageUnit(0, i, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);

                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
                GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);
            }
            #endregion

            #region Upsample
            shaderProgram.Upload(1, (int)Stage.Upsample);
            downscaleTexture.BindToUnit(1);
           
            size = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, Lod - 2);
            shaderProgram.Upload(0, Lod - 1);
            upsampleTexture.BindToImageUnit(0, Lod - 2, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
            GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);

            upsampleTexture.BindToUnit(1);
            for (int i = Lod - 3; i >= 0; i--)
            {
                size = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, i);

                shaderProgram.Upload(0, i + 1);
                upsampleTexture.BindToImageUnit(0, i, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);

                GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
                GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);
            }
            #endregion
        }

        public void SetSize(int width, int height)
        {
            downscaleTexture.Dispose();
            
            downscaleTexture = new Texture(TextureTarget2d.Texture2D);
            downscaleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            downscaleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            downscaleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, Lod);


            upsampleTexture.Dispose();

            upsampleTexture = new Texture(TextureTarget2d.Texture2D);
            upsampleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            upsampleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upsampleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, Lod);
        }
    }
}
