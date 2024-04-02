using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;
using IDKEngine.OpenGL;

namespace IDKEngine.Render
{
    static class SkyBoxManager
    {
        public enum SkyBoxMode
        {
            ExternalAsset,
            InternalAtmosphericScattering,
        }

        public static string[] Paths;
        public static Texture SkyBoxTexture => AtmosphericScatterer != null ? AtmosphericScatterer.Result : externalSkyBoxTexture;
        public static AtmosphericScatterer AtmosphericScatterer { get; private set; }


        private static Texture externalSkyBoxTexture;
        public static TypedBuffer<long> skyBoxTextureBuffer;
        public static void Init(SkyBoxMode skyBoxMode, string[] paths = null)
        {
            skyBoxTextureBuffer = new TypedBuffer<long>();
            skyBoxTextureBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 4);
            skyBoxTextureBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1, 0);

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

                externalSkyBoxTexture = new Texture(TextureTarget2d.TextureCubeMap);
                externalSkyBoxTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);

                if (!Helper.LoadCubemap(externalSkyBoxTexture, Paths, SizedInternalFormat.Srgb8))
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

                AtmosphericScatterer = new AtmosphericScatterer(128);
                AtmosphericScatterer.Compute();
            }

            SkyBoxTexture.EnableSeamlessCubemapARB_AMD(true);
            skyBoxTextureBuffer.UploadElements(SkyBoxTexture.GetTextureHandleARB());

            _skyBoxMode = skyBoxMode;
        }

        public static SkyBoxMode GetSkyBoxMode()
        {
            return _skyBoxMode;
        }

        public static void Dispose()
        {
            if (AtmosphericScatterer != null) AtmosphericScatterer.Dispose();
            if (externalSkyBoxTexture != null) externalSkyBoxTexture.Dispose();
            if (skyBoxTextureBuffer != null) skyBoxTextureBuffer.Dispose();
        }
    }
}
