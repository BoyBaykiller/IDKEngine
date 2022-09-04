using System;
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

                if (SkyBoxTexture != null)
                {
                    skyBoxTextureUBO.GetSubData(0, sizeof(ulong), out ulong currentTextureHandle);
                    Texture.UnmakeTextureHandleResidentARB(currentTextureHandle);
                }

                if (_isExternalSkyBox)
                {
                    if (AtmosphericScatterer != null)
                    {
                        AtmosphericScatterer.Dispose();
                        AtmosphericScatterer = null;
                    }

                    externalSkyBox = new Texture(TextureTarget2d.TextureCubeMap);
                    externalSkyBox.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
                    Helper.ParallelLoadCubemap(externalSkyBox, Paths, (SizedInternalFormat)PixelInternalFormat.CompressedSrgbAlphaBptcUnorm);
                }
                else
                {
                    if (externalSkyBox != null)
                    {
                        externalSkyBox.Dispose();
                        externalSkyBox = null;
                    }

                    AtmosphericScatterer = new AtmosphericScatterer(256);
                    AtmosphericScatterer.Compute();
                }

                /// Info: https://stackoverflow.com/questions/68735879/opengl-using-bindless-textures-on-sampler2d-disables-texturecubemapseamless
                SkyBoxTexture.SetSeamlessCubeMapPerTextureARB_AMD(true);
                skyBoxTextureUBO.SubData(0, sizeof(ulong), SkyBoxTexture.GenTextureHandleARB());
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
