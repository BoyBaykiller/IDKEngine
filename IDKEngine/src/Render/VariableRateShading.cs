using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;
using IDKEngine.OpenGL;

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
        public VariableRateShading(Vector2i size, NvShadingRateImage[] shadingRatePalette)
        {
            ShadingRatePalette = shadingRatePalette;
            SetSize(size);
        }

        public void SetSize(Vector2i size)
        {
            size.X = (int)MathF.Ceiling((float)size.X / TILE_SIZE);
            size.Y = (int)MathF.Ceiling((float)size.Y / TILE_SIZE);

            if (Result != null) Result.Dispose();
            Result = new Texture(Texture.Type.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Result.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R8Uint);
        }

        public void Dispose()
        {
            Result.Dispose();
        }
    }
}
