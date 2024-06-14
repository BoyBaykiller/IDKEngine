using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BBLogger;
using BBOpenGL;

namespace IDKEngine.Render
{
    static class SkyBoxManager
    {
        public enum SkyBoxMode : int
        {
            ExternalAsset,
            InternalAtmosphericScattering,
        }

        public static string[] Paths;
        public static BBG.Texture SkyBoxTexture => AtmosphericScatterer != null ? AtmosphericScatterer.Result : externalSkyBoxTexture;
        public static AtmosphericScatterer AtmosphericScatterer { get; private set; }


        private static BBG.Texture externalSkyBoxTexture;
        public static BBG.TypedBuffer<ulong> skyBoxTextureBuffer;
        public static void Initialize(SkyBoxMode skyBoxMode, string[] paths = null)
        {
            skyBoxTextureBuffer = new BBG.TypedBuffer<ulong>();
            skyBoxTextureBuffer.BindBufferBase(BBG.Buffer.BufferTarget.Uniform, 4);
            skyBoxTextureBuffer.ImmutableAllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.Synced, 1, 0);

            Paths = paths;
            SetSkyBoxMode(skyBoxMode);
        }

        private static SkyBoxMode _skyBoxMode;
        public static void SetSkyBoxMode(SkyBoxMode skyBoxMode)
        {
            if (skyBoxMode == SkyBoxMode.ExternalAsset)
            {
                if (AtmosphericScatterer != null)
                {
                    AtmosphericScatterer.Dispose();
                    AtmosphericScatterer = null;
                }

                externalSkyBoxTexture = new BBG.Texture(BBG.Texture.Type.Cubemap);
                externalSkyBoxTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);

                if (!LoadCubemap(externalSkyBoxTexture, BBG.Texture.InternalFormat.R8G8B8SRgba, Paths))
                {
                    skyBoxMode = SkyBoxMode.InternalAtmosphericScattering;
                }
            }

            if (skyBoxMode == SkyBoxMode.InternalAtmosphericScattering)
            {
                if (externalSkyBoxTexture != null)
                {
                    externalSkyBoxTexture.Dispose();
                    externalSkyBoxTexture = null;
                }

                AtmosphericScatterer = new AtmosphericScatterer(128, AtmosphericScatterer.GpuSettings.Default);
                AtmosphericScatterer.Compute();
            }

            SkyBoxTexture.TryEnableSeamlessCubemap(true);
            skyBoxTextureBuffer.UploadElements(SkyBoxTexture.GetTextureHandleARB());

            _skyBoxMode = skyBoxMode;
        }

        private static unsafe bool LoadCubemap(BBG.Texture texture, BBG.Texture.InternalFormat format, string[] imagePaths)
        {
            if (imagePaths == null)
            {
                Logger.Log(Logger.LogLevel.Error, $"Cubemap {nameof(imagePaths)} is null");
                return false;
            }
            if (texture.TextureType != BBG.Texture.Type.Cubemap)
            {
                Logger.Log(Logger.LogLevel.Error, $"Texture must be of type {BBG.Texture.Type.Cubemap}");
                return false;
            }
            if (imagePaths.Length != 6)
            {
                Logger.Log(Logger.LogLevel.Error, "Number of cubemap images must be equal to six");
                return false;
            }
            if (!imagePaths.All(p => File.Exists(p)))
            {
                Logger.Log(Logger.LogLevel.Error, "At least one of the specified cubemap images is not found");
                return false;
            }

            ImageLoader.ImageResult[] images = new ImageLoader.ImageResult[6];
            Parallel.For(0, images.Length, i =>
            {
                ImageLoader.ImageResult imageResult = ImageLoader.Load(imagePaths[i], 3);
                images[i] = imageResult;
            });

            if (!images.All(i => i.Header.Width == i.Header.Height && i.Header.Width == images[0].Header.Width))
            {
                Logger.Log(Logger.LogLevel.Error, "Cubemap images must be squares and each texture must be of the same size");
                return false;
            }
            int size = images[0].Header.Width;

            texture.ImmutableAllocate(size, size, 1, format);
            for (int i = 0; i < 6; i++)
            {
                using ImageLoader.ImageResult imageResult = images[i];
                texture.Upload3D(
                    size, size, 1,
                    BBG.Texture.NumChannelsToPixelFormat(imageResult.Channels),
                    BBG.Texture.PixelType.UByte,
                    imageResult.RawPixels,
                    0, 0, 0, i
                );
            }

            return true;
        }

        public static SkyBoxMode GetSkyBoxMode()
        {
            return _skyBoxMode;
        }

        public static void Terminate()
        {
            if (AtmosphericScatterer != null) AtmosphericScatterer.Dispose();
            if (externalSkyBoxTexture != null) externalSkyBoxTexture.Dispose();
            if (skyBoxTextureBuffer != null) skyBoxTextureBuffer.Dispose();
        }
    }
}
