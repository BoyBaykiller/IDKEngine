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

        public struct GpuSettings
        {
            public float Threshold;
            public float MaxColor;

            public static GpuSettings Default = new GpuSettings()
            {
                Threshold = 1.0f,
                MaxColor = 2.8f
            };
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

        public GpuSettings Settings;

        public Texture Result => upsampleTexture;

        private Texture downscaleTexture;
        private Texture upsampleTexture;
        private readonly ShaderProgram shaderProgram;
        private readonly TypedBuffer<GpuSettings> bufferGpuSettings;
        public Bloom(int width, int height, in GpuSettings settings, int minusLods = 3)
        {
            shaderProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "Bloom/compute.glsl"));

            bufferGpuSettings = new TypedBuffer<GpuSettings>();
            bufferGpuSettings.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(width, height);

            MinusLods = minusLods;
            Settings = settings;
        }

        public void Compute(Texture src)
        {
            bufferGpuSettings.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            bufferGpuSettings.UploadElements(Settings);

            shaderProgram.Use();
            int currentWriteLod = 0;

            // Downsampling
            {
                src.BindToUnit(0);
                shaderProgram.Upload(1, (int)Stage.Downsample);
            
                {
                    shaderProgram.Upload(0, currentWriteLod);
                    downscaleTexture.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, downscaleTexture.SizedInternalFormat);
                
                    Vector3i mipLevelSize = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, currentWriteLod);
                    GL.DispatchCompute((mipLevelSize.X + 8 - 1) / 8, (mipLevelSize.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                    currentWriteLod++;
                }

                downscaleTexture.BindToUnit(0);
                for (; currentWriteLod < downscaleTexture.Levels; currentWriteLod++)
                {
                    shaderProgram.Upload(0, currentWriteLod - 1);
                    downscaleTexture.BindToImageUnit(0, currentWriteLod, false, 0, TextureAccess.WriteOnly, downscaleTexture.SizedInternalFormat);

                    Vector3i mipLevelSize = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, currentWriteLod);
                    GL.DispatchCompute((mipLevelSize.X + 8 - 1) / 8, (mipLevelSize.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                }
            }

            // Upsampling
            {
                currentWriteLod = downscaleTexture.Levels - 2;
                downscaleTexture.BindToUnit(1);
                shaderProgram.Upload(1, (int)Stage.Upsample);

                {
                    shaderProgram.Upload(0, currentWriteLod + 1);
                    upsampleTexture.BindToImageUnit(0, currentWriteLod, false, 0, TextureAccess.WriteOnly, upsampleTexture.SizedInternalFormat);
                    
                    Vector3i mipLevelSize = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, currentWriteLod);
                    GL.DispatchCompute((mipLevelSize.X + 8 - 1) / 8, (mipLevelSize.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                    currentWriteLod--;
                }

                upsampleTexture.BindToUnit(1);
                for (; currentWriteLod >= 0; currentWriteLod--)
                {
                    shaderProgram.Upload(0, currentWriteLod + 1);
                    upsampleTexture.BindToImageUnit(0, currentWriteLod, false, 0, TextureAccess.WriteOnly, upsampleTexture.SizedInternalFormat);

                    Vector3i mipLevelSize = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, currentWriteLod);
                    GL.DispatchCompute((mipLevelSize.X + 8 - 1) / 8, (mipLevelSize.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                }
            }
        }

        public void SetSize(int width, int height)
        {
            width = (int)MathF.Ceiling(width / 2);
            height = (int)MathF.Ceiling(height / 2);

            int levels = Math.Max(Texture.GetMaxMipmapLevel(width, height, 1) - MinusLods, 2);

            if (downscaleTexture != null) downscaleTexture.Dispose();
            downscaleTexture = new Texture(TextureTarget2d.Texture2D);
            downscaleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            downscaleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            downscaleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, levels);

            if (upsampleTexture != null) upsampleTexture.Dispose();
            upsampleTexture = new Texture(TextureTarget2d.Texture2D);
            upsampleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            upsampleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upsampleTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f, levels - 1);
        }

        public void Dispose()
        {
            downscaleTexture.Dispose();
            upsampleTexture.Dispose();
            shaderProgram.Dispose();
            bufferGpuSettings.Dispose();
        }
    }
}
