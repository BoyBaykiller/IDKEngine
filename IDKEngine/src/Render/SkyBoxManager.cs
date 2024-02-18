using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    static class SkyBoxManager
    {
        private static bool _isExternalSkyBox;
        public static bool IsExternalSkyBox
        {
            get => _isExternalSkyBox;

            set
            {
                if (_isExternalSkyBox == value) return;
                _isExternalSkyBox = value;

                if (_isExternalSkyBox)
                {
                    if (AtmosphericScatterer != null)
                    {
                        AtmosphericScatterer.Dispose();
                        AtmosphericScatterer = null;
                    }

                    externalSkyBox = new Texture(TextureTarget2d.TextureCubeMap);
                    externalSkyBox.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
                    Helper.ParallelLoadCubemap(externalSkyBox, Paths, SizedInternalFormat.Srgb8);
                }
                else
                {
                    if (externalSkyBox != null)
                    {
                        externalSkyBox.Dispose();
                        externalSkyBox = null;
                    }

                    AtmosphericScatterer = new AtmosphericScatterer(128);
                    AtmosphericScatterer.Compute();
                }

                SkyBoxTexture.EnableSeamlessCubemapARB_AMD(true);
                skyBoxTextureBuffer.UploadElements(SkyBoxTexture.GetTextureHandleARB());
            }
        }

        public static Texture SkyBoxTexture => AtmosphericScatterer != null ? AtmosphericScatterer.Result : externalSkyBox;

        public static string[] Paths;
        public static AtmosphericScatterer AtmosphericScatterer { get; private set; }

        private static Texture externalSkyBox;
        public static TypedBuffer<ulong> skyBoxTextureBuffer;
        public static void Init(string[] paths = null)
        {
            skyBoxTextureBuffer = new TypedBuffer<ulong>();
            skyBoxTextureBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 4);
            skyBoxTextureBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1, 0ul);

            if (paths != null)
            {
                Paths = paths;
                IsExternalSkyBox = true;
            }
        }

        public static void Dispose()
        {
            if (AtmosphericScatterer != null) AtmosphericScatterer.Dispose();
            if (externalSkyBox != null) externalSkyBox.Dispose();
            if (skyBoxTextureBuffer != null) skyBoxTextureBuffer.Dispose();
        }
    }
}
