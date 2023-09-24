using System;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    abstract class VariableRateShading : IDisposable
    {
        // Defined by https://registry.khronos.org/OpenGL/extensions/NV/NV_shading_rate_image.txt
        public const int TILE_SIZE = 16;

        public static readonly bool HAS_VARIABLE_RATE_SHADING = Helper.IsExtensionsAvailable("GL_NV_shading_rate_image");

        /// <summary>
        /// GL_NV_shading_rate_image must be available for this to take effect
        /// </summary>
        public static void Activate(VariableRateShading variableRateShading, int viewport = 0)
        {
            if (HAS_VARIABLE_RATE_SHADING)
            {
                GL.Enable((EnableCap)NvShadingRateImage.ShadingRateImageNv);
                GL.NV.ShadingRateImagePalette(viewport, 0, variableRateShading.ShadingRatePalette.Length, ref variableRateShading.ShadingRatePalette[0]);
                GL.NV.ShadingRateImageBarrier(true);
                GL.NV.BindShadingRateImage(variableRateShading.Result.ID);
            }
        }

        /// <summary>
        /// GL_NV_shading_rate_image must be available for this to take effect
        /// </summary>
        public static void Deactivate()
        {
            if (HAS_VARIABLE_RATE_SHADING)
            {
                GL.Disable((EnableCap)NvShadingRateImage.ShadingRateImageNv);
            }
        }
        
        public Texture Result;
        public NvShadingRateImage[] ShadingRatePalette;
        public VariableRateShading(int width, int height, NvShadingRateImage[] shadingRatePalette)
        {
            ShadingRatePalette = shadingRatePalette;
            SetSize(width, height);
        }

        public void SetSize(int width, int height)
        {
            width = (int)MathF.Ceiling((float)width / TILE_SIZE);
            height = (int)MathF.Ceiling((float)height / TILE_SIZE);

            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.R8ui);
        }

        public void Dispose()
        {
            Result.Dispose();
        }
    }
}
