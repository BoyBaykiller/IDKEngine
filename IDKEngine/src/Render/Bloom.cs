using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

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
                SetSize(new Vector2i(Result.Width, Result.Height) * 2);
            }
        }

        public GpuSettings Settings;

        public Texture Result => upsampleTexture;

        private Texture downscaleTexture;
        private Texture upsampleTexture;
        private readonly AbstractShaderProgram shaderProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public Bloom(Vector2i size, in GpuSettings settings, int minusLods = 3)
        {
            shaderProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "Bloom/compute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            MinusLods = minusLods;
            Settings = settings;
        }

        public void Compute(Texture src)
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            shaderProgram.Use();
            int currentWriteLod = 0;

            // Downsampling
            {
                src.BindToUnit(0);
                shaderProgram.Upload(1, (int)Stage.Downsample);
            
                {
                    shaderProgram.Upload(0, currentWriteLod);
                    downscaleTexture.BindToImageUnit(0, downscaleTexture.TextureFormat);
                
                    Vector3i mipLevelSize = Texture.GetMipMapLevelSize(downscaleTexture.Width, downscaleTexture.Height, 1, currentWriteLod);
                    GL.DispatchCompute((mipLevelSize.X + 8 - 1) / 8, (mipLevelSize.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                    currentWriteLod++;
                }

                downscaleTexture.BindToUnit(0);
                for (; currentWriteLod < downscaleTexture.Levels; currentWriteLod++)
                {
                    shaderProgram.Upload(0, currentWriteLod - 1);
                    downscaleTexture.BindToImageUnit(0, downscaleTexture.TextureFormat, 0, false, currentWriteLod);

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
                    upsampleTexture.BindToImageUnit(0, upsampleTexture.TextureFormat, 0,false, currentWriteLod);
                    
                    Vector3i mipLevelSize = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, currentWriteLod);
                    GL.DispatchCompute((mipLevelSize.X + 8 - 1) / 8, (mipLevelSize.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                    currentWriteLod--;
                }

                upsampleTexture.BindToUnit(1);
                for (; currentWriteLod >= 0; currentWriteLod--)
                {
                    shaderProgram.Upload(0, currentWriteLod + 1);
                    upsampleTexture.BindToImageUnit(0, upsampleTexture.TextureFormat, 0, false, currentWriteLod);

                    Vector3i mipLevelSize = Texture.GetMipMapLevelSize(upsampleTexture.Width, upsampleTexture.Height, 1, currentWriteLod);
                    GL.DispatchCompute((mipLevelSize.X + 8 - 1) / 8, (mipLevelSize.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                }
            }
        }

        public void SetSize(Vector2i size)
        {
            size.X = (int)MathF.Ceiling(size.X / 2);
            size.Y = (int)MathF.Ceiling(size.Y / 2);

            int levels = Math.Max(Texture.GetMaxMipmapLevel(size.X, size.Y, 1) - MinusLods, 2);

            if (downscaleTexture != null) downscaleTexture.Dispose();
            downscaleTexture = new Texture(Texture.Type.Texture2D);
            downscaleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            downscaleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            downscaleTexture.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R16G16B16A16Float, levels);

            if (upsampleTexture != null) upsampleTexture.Dispose();
            upsampleTexture = new Texture(Texture.Type.Texture2D);
            upsampleTexture.SetFilter(TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear);
            upsampleTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upsampleTexture.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R16G16B16A16Float, levels - 1);
        }

        public void Dispose()
        {
            downscaleTexture.Dispose();
            upsampleTexture.Dispose();
            shaderProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
