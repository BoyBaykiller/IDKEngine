using IDKEngine.Render;
using IDKEngine.Render.Objects;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine
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
                skyBoxTextureUBO.SubData(0, sizeof(ulong), SkyBoxTexture.GetTextureHandleARB());
            }
        }

        public static Texture SkyBoxTexture => AtmosphericScatterer != null ? AtmosphericScatterer.Result : externalSkyBox;

        public static string[] Paths;
        public static AtmosphericScatterer AtmosphericScatterer { get; private set; }

        private static Texture externalSkyBox;
        private static BufferObject skyBoxTextureUBO;
        public static void Init(string[] paths = null)
        {
            skyBoxTextureUBO = new BufferObject();
            skyBoxTextureUBO.BindBufferBase(BufferRangeTarget.UniformBuffer, 4);
            skyBoxTextureUBO.ImmutableAllocate(sizeof(ulong), 0ul, BufferStorageFlags.DynamicStorageBit);

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
            if (skyBoxTextureUBO != null) skyBoxTextureUBO.Dispose();
        }
    }
}
