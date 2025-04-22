using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;

namespace IDKEngine.Render
{
    static class SkyBoxManager
    {
        public enum SkyBoxMode : int
        {
            ExternalAsset,
            InternalAtmosphericScattering,
        }

        public static string[] SkyBoxImagePaths;

        public static BBG.Texture SkyBoxTexture => AtmosphericScatterer != null ? AtmosphericScatterer.Result : externalCubemapTexture;
        public static AtmosphericScatterer AtmosphericScatterer { get; private set; }

        private static BBG.Texture externalCubemapTexture;
        private static BBG.TypedBuffer<BBG.Texture.BindlessHandle> skyBoxTextureBuffer;
        private static BBG.AbstractShaderProgram unprojectEquirectangularProgram;
        public static void Initialize()
        {
            skyBoxTextureBuffer = new BBG.TypedBuffer<BBG.Texture.BindlessHandle>();
            FSR2WorkaroundRebindUBO();
            skyBoxTextureBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);

            unprojectEquirectangularProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "UnprojectEquirectangular/compute.glsl"));

            SetSkyBoxMode(SkyBoxMode.InternalAtmosphericScattering);
        }

        private static SkyBoxMode _skyBoxMode;
        public static void SetSkyBoxMode(SkyBoxMode skyBoxMode)
        {
            if (skyBoxMode == SkyBoxMode.ExternalAsset)
            {
                externalCubemapTexture = new BBG.Texture(BBG.Texture.Type.Cubemap);
                externalCubemapTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);

                if (!LoadSkyBox(SkyBoxImagePaths))
                {
                    skyBoxMode = SkyBoxMode.InternalAtmosphericScattering;
                }
            }
            else
            {
                if (externalCubemapTexture != null)
                {
                    externalCubemapTexture.Dispose();
                    externalCubemapTexture = null;
                }
            }

            if (skyBoxMode == SkyBoxMode.InternalAtmosphericScattering)
            {
                AtmosphericScatterer = new AtmosphericScatterer(128, new AtmosphericScatterer.GpuSettings());
                AtmosphericScatterer.Compute();
            }
            else
            {
                if (AtmosphericScatterer != null)
                {
                    AtmosphericScatterer.Dispose();
                    AtmosphericScatterer = null;
                }
            }

            SkyBoxTexture.TryEnableSeamlessCubemap(true);
            skyBoxTextureBuffer.UploadElements(SkyBoxTexture.GetTextureHandleARB());

            _skyBoxMode = skyBoxMode;
        }

        public static SkyBoxMode GetSkyBoxMode()
        {
            return _skyBoxMode;
        }

        public static void FSR2WorkaroundRebindUBO()
        {
            skyBoxTextureBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 5);
        }

        private static unsafe bool LoadSkyBox(string[] imagePaths)
        {
            if (imagePaths == null)
            {
                Logger.Log(Logger.LogLevel.Error, $"SkyBox {nameof(imagePaths)} is null");
                return false;
            }
            if (!imagePaths.All(p => File.Exists(p)))
            {
                Logger.Log(Logger.LogLevel.Error, "At least one of the skybox images is not found");
                return false;
            }

            bool success = false;
            if (imagePaths.Length == 1)
            {
                success = LoadSkyBoxEquirectangular(imagePaths[0]);
            }
            if (imagePaths.Length == 6)
            {
                success = LoadSkyBoxImages(imagePaths);
            }
            if (success)
            {
                SkyBoxImagePaths = imagePaths;
            }

            return success;
        }

        private static unsafe bool LoadSkyBoxEquirectangular(string hrdPath)
        {
            using ImageLoader.ImageResult hdrImage = ImageLoader.Load(hrdPath, ImageLoader.ColorComponents.RGB);
            if (!hdrImage.IsLoadedSuccesfully)
            {
                Logger.Log(Logger.LogLevel.Error, $"Hdr image could not be loaded");
                return false;
            }

            externalCubemapTexture.Allocate(1536, 1536, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);
            using BBG.Texture equirectangularTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);

            BBG.Computing.Compute("Equirectangular to Cubemap", () =>
            {
                equirectangularTexture.Allocate(hdrImage.Header.Width, hdrImage.Header.Height, 1, externalCubemapTexture.Format);
                equirectangularTexture.Upload2D(
                    hdrImage.Header.Width, hdrImage.Header.Height,
                    BBG.Texture.NumChannelsToPixelFormat(hdrImage.Header.Channels),
                    BBG.Texture.PixelType.Float,
                    hdrImage.Memory
                );

                BBG.Cmd.BindImageUnit(externalCubemapTexture, 0, 0, true);
                BBG.Cmd.BindTextureUnit(equirectangularTexture, 0);
                BBG.Cmd.UseShaderProgram(unprojectEquirectangularProgram);

                BBG.Computing.Dispatch(MyMath.DivUp(externalCubemapTexture.Width, 8), MyMath.DivUp(externalCubemapTexture.Height, 8), 6);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });

            return true;
        }

        private static unsafe bool LoadSkyBoxImages(string[] imagePaths)
        {
            if (imagePaths.Length != 6)
            {
                Logger.Log(Logger.LogLevel.Error, "Number of skybox images must be equal to six");
                return false;
            }

            ImageLoader.ImageResult[] images = new ImageLoader.ImageResult[6];
            Parallel.For(0, images.Length, i =>
            {
                images[i] = ImageLoader.Load(imagePaths[i], ImageLoader.ColorComponents.RGB);
            });

            if (images.Any(it => !it.IsLoadedSuccesfully))
            {
                Logger.Log(Logger.LogLevel.Error, $"At least one of the skybox images could not be loaded");
                return false;
            }
            if (!images.All(it => it.Header.Width == it.Header.Height && it.Header.Width == images[0].Header.Width))
            {
                Logger.Log(Logger.LogLevel.Error, "Skybox images must be squares and each texture must be of the same size");
                return false;
            }
            int size = images[0].Header.Width;

            externalCubemapTexture.Allocate(size, size, 1, BBG.Texture.InternalFormat.R8G8B8A8SRgb);
            for (int i = 0; i < 6; i++)
            {
                using ImageLoader.ImageResult imageResult = images[i];
                externalCubemapTexture.Upload3D(
                    size, size, 1,
                    BBG.Texture.NumChannelsToPixelFormat(imageResult.Header.Channels),
                    BBG.Texture.PixelType.Ubyte,
                    imageResult.Memory,
                    0, 0, 0, i
                );
            }

            return true;
        }

        public static void Terminate()
        {
            if (AtmosphericScatterer != null) AtmosphericScatterer.Dispose();
            if (externalCubemapTexture != null) externalCubemapTexture.Dispose();
            if (skyBoxTextureBuffer != null) skyBoxTextureBuffer.Dispose();
        }
    }
}
