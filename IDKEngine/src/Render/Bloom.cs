using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Bloom : IDisposable
    {
        private enum Stage : int
        {
            Downsample,
            Upsample,
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

        private int _minusLods;
        public int MinusLods
        {
            get => _minusLods;

            set
            {
                _minusLods = value;
                // Multiply with 2 to get size of original src texture
                SetSize(Result.Width * 2, Result.Height * 2);
            }
        }

        public Texture Result => upsampleTexture;

        private Texture downscaleTexture;
        private Texture upsampleTexture;
        private readonly ShaderProgram shaderProgram;
        private int lodCount;
        public Bloom(int width, int height, float threshold, float clamp, int minusLods = 3)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Bloom/compute.glsl")));

            SetSize(width, height);

            MinusLods = minusLods;
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
            downscaleTexture.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, downscaleTexture.SizedInternalFormat);
            GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 8 - 1) / 8, 1);

            downscaleTexture.BindToUnit(0);
            for (int i = 1; i < lodCount; i++)
            {
                size = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, i);

                shaderProgram.Upload(3, i - 1);
                downscaleTexture.BindToImageUnit(0, i, false, 0, TextureAccess.WriteOnly, downscaleTexture.SizedInternalFormat);

                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 8 - 1) / 8, 1);
            }
            #endregion
            
            #region Upsample
            downscaleTexture.BindToUnit(1);
            shaderProgram.Upload(4, (int)Stage.Upsample);

            size = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, lodCount - 2);
            shaderProgram.Upload(3, lodCount - 1);
            upsampleTexture.BindToImageUnit(0, lodCount - 2, false, 0, TextureAccess.WriteOnly, upsampleTexture.SizedInternalFormat);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
            GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 8 - 1) / 8, 1);

            upsampleTexture.BindToUnit(1);
            for (int i = lodCount - 3; i >= 0; i--)
            {
                size = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, i);

                shaderProgram.Upload(3, i + 1);
                upsampleTexture.BindToImageUnit(0, i, false, 0, TextureAccess.WriteOnly, upsampleTexture.SizedInternalFormat);

                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                GL.DispatchCompute((size.X + 8 - 1) / 8, (size.Y + 8 - 1) / 8, 1);
            }
            #endregion
        }

        public void SetSize(int width, int height)
        {
            width /= 2;
            height /= 2;

            lodCount = Math.Max(Texture.GetMaxMipmapLevel(width, height, 1) - MinusLods, 2);

            if (downscaleTexture != null) downscaleTexture.Dispose();
            downscaleTexture = new Texture(TextureTarget2d.Texture2D);
            downscaleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            downscaleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            downscaleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, lodCount);

            if (upsampleTexture != null) upsampleTexture.Dispose();
            upsampleTexture = new Texture(TextureTarget2d.Texture2D);
            upsampleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            upsampleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upsampleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, lodCount - 1);
        }

        public void Dispose()
        {
            downscaleTexture.Dispose();
            upsampleTexture.Dispose();
            shaderProgram.Dispose();
        }
    }
}
