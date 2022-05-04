using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class VariableRateShading
    {
        // Definied by spec
        public const int TILE_SIZE = 16;

        public static readonly bool NV_SHADING_RATE_IMAGE = Helper.IsExtensionsAvailable("GL_NV_shading_rate_image");

        private static bool _isEnabled;
        /// <summary>
        /// GL_NV_shading_rate_image must be available for this to take effect
        /// </summary>
        public static bool IsEnabled
        {
            get => _isEnabled;

            set
            {
                if (NV_SHADING_RATE_IMAGE)
                {
                    if (_isEnabled == value)
                        return;

                    _isEnabled = value;
                    if (_isEnabled)
                    {
                        GL.Enable((EnableCap)NvShadingRateImage.ShadingRateImageNv);
                    }
                    else
                    {
                        GL.Disable((EnableCap)NvShadingRateImage.ShadingRateImageNv);
                    }
                }
            }
        }

        private bool _isDebug;
        public bool IsDebug
        {
            get => _isDebug;

            set
            {
                _isDebug = value;
                shaderProgram.Upload("IsDebug", IsDebug);
            }
        }

        private float _aggressiveness;
        public float Aggressiveness
        {
            get => _aggressiveness;

            set
            {
                _aggressiveness = value;
                shaderProgram.Upload("Aggressiveness", Aggressiveness);
            }
        }

        public Texture Result;
        private readonly ShaderProgram shaderProgram;
        private int width;
        private int height;
        public unsafe VariableRateShading(Shader classificationComputeShader, int width, int height, float aggressiveness = 0.5f)
        {
            shaderProgram = new ShaderProgram(classificationComputeShader);

            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            // Shading rate texture must be imuutable by spec
            Result.ImmutableAllocate(width / 16, height / 16, 1, SizedInternalFormat.R8ui);
            
            this.width = width;
            this.height = height;
            Aggressiveness = aggressiveness;
        }

        /// <summary>
        /// NV_shading_rate_image must be available for this to take effect
        /// </summary>
        /// <param name="shadingRates"></param>
        /// <param name="viewport"></param>
        public static void SetShadingRatePaletteNV(Span<NvShadingRateImage> shadingRates, int viewport = 0)
        {
            if (NV_SHADING_RATE_IMAGE)
            {
                GL.NV.ShadingRateImagePalette(viewport, 0, shadingRates.Length, ref shadingRates[0]);
            }
        }

        /// <summary>
        /// NV_shading_rate_image must be available for this to take effect
        /// </summary>
        public void BindShadingRateImageNV()
        {
            if (NV_SHADING_RATE_IMAGE)
            {
                GL.NV.BindShadingRateImage(Result.ID);
            }
        }

        public unsafe void Compute(Texture shaded, Texture velocity)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R8ui);
            shaded.BindToImageUnit(1, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);
            velocity.BindToUnit(0);
            shaded.BindToUnit(1);

            shaderProgram.Use();
            GL.DispatchCompute((width + TILE_SIZE - 1) / TILE_SIZE, (height + TILE_SIZE - 1) / TILE_SIZE, 1);

            if (NV_SHADING_RATE_IMAGE)
                GL.NV.ShadingRateImageBarrier(true);
        }

        public void SetSize(int width, int height)
        {
            // Shading rate texture must be immutable by spec so recreate the whole texture
            if (NV_SHADING_RATE_IMAGE)
            {
                GL.NV.BindShadingRateImage(0);
            }
            Result.Dispose();
            
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Result.ImmutableAllocate(width / 16, height / 16, 1, SizedInternalFormat.R8ui);

            BindShadingRateImageNV();
            this.width = width;
            this.height = height;
        }
    }
}
