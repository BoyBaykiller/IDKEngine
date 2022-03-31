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

        private float _threshold;
        public float Threshold
        {
            get => _threshold;

            set
            {
                _threshold = value;
                shaderProgram.Upload("Threshold", _threshold);
            }
        }

        private float _clamp;
        public float Clamp
        {
            get => _clamp;

            set
            {
                _clamp = value;
                shaderProgram.Upload("Clamp", _clamp);
            }
        }
        private enum Stage : int
        {
            Downsample = 0,
            Upsample = 1,
        }

        public Texture Result => upsampleTexture;

        private Texture downscaleTexture;
        private Texture upsampleTexture;
        private readonly ShaderProgram shaderProgram;
        public Bloom(int width, int height, float threshold, float clamp)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Bloom/compute.glsl")));

            width = (int)(width / 2.0f);
            height = (int)(height / 2.0f);
            _lod = System.Math.Max(Texture.GetMaxMipMaplevel(width, height, 1) - 3, 1);

            downscaleTexture = new Texture(TextureTarget2d.Texture2D);
            downscaleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            downscaleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            downscaleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, _lod);

            upsampleTexture = new Texture(TextureTarget2d.Texture2D);
            upsampleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            upsampleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upsampleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, _lod);

            Threshold = threshold;
            Clamp = clamp;
        }

        public void Compute(Texture src)
        {
            shaderProgram.Use();

            #region Downsample
            src.BindToUnit(0);
            shaderProgram.Upload(4, (int)Stage.Downsample);

            Vector3i size = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, 0);
            shaderProgram.Upload(3, 0);
            downscaleTexture.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);

            downscaleTexture.BindToUnit(0);
            for (int i = 1; i < Lod; i++)
            {
                size = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, i);

                shaderProgram.Upload(3, i - 1);
                downscaleTexture.BindToImageUnit(0, i, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);
            }
            #endregion

            #region Upsample
            downscaleTexture.BindToUnit(1);
            shaderProgram.Upload(4, (int)Stage.Upsample);

            size = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, Lod - 2);
            shaderProgram.Upload(3, Lod - 1);
            upsampleTexture.BindToImageUnit(0, Lod - 2, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
            GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);

            upsampleTexture.BindToUnit(1);
            for (int i = Lod - 3; i >= 0; i--)
            {
                size = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, i);

                shaderProgram.Upload(3, i + 1);
                upsampleTexture.BindToImageUnit(0, i, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 4 - 1) / 4, 1);
            }
            #endregion
        }

        public void SetSize(int width, int height)
        {
            _lod = System.Math.Max(Texture.GetMaxMipMaplevel(width, height, 1) - 3, 1);

            downscaleTexture.Dispose();
            
            downscaleTexture = new Texture(TextureTarget2d.Texture2D);
            downscaleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            downscaleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            downscaleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, _lod);


            upsampleTexture.Dispose();

            upsampleTexture = new Texture(TextureTarget2d.Texture2D);
            upsampleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            upsampleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upsampleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, _lod);
        }
    }
}
