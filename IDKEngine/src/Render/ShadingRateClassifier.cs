using System;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ShadingRateClassifier : IDisposable
    {
        // Defined by spec
        public const int TILE_SIZE = 16;

        public static readonly bool HAS_VARIABLE_RATE_SHADING = Helper.IsExtensionsAvailable("GL_NV_shading_rate_image");

        // used in shader and client code - keep in sync!
        public enum DebugMode
        {
            NoDebug = 0,
            ShadingRate = 1,
            Speed = 2,
            Luminance = 3,
            LuminanceVariance = 4,
        }
        

        private static bool _isEnabled;
        /// <summary>
        /// GL_NV_shading_rate_image must be available for this to take effect
        /// </summary>
        public static bool IsEnabled
        {
            get => _isEnabled;

            set
            {
                if (HAS_VARIABLE_RATE_SHADING)
                {
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


        private DebugMode _debug;
        public DebugMode DebugValue
        {
            get => _debug;

            set
            {
                _debug = value;
                shaderProgram.Upload("DebugMode", (int)DebugValue);
            }
        }

        private float _speedFactor;
        public float SpeedFactor
        {
            get => _speedFactor;

            set
            {
                _speedFactor = value;
                shaderProgram.Upload("SpeedFactor", SpeedFactor);
            }
        }

        private float _lumVarianceFactor;
        public float LumVarianceFactor
        {
            get => _lumVarianceFactor;

            set
            {
                _lumVarianceFactor = value;
                shaderProgram.Upload("LumVarianceFactor", LumVarianceFactor);
            }
        }

        public Texture Result;
        public NvShadingRateImage[] ShadingRates;
        private readonly ShaderProgram shaderProgram;
        public ShadingRateClassifier(NvShadingRateImage[] shadingRates, Shader classificationComputeShader, int width, int height, float lumVarianceFactor = 0.025f, float speedFactor = 0.2f)
        {
            shaderProgram = new ShaderProgram(classificationComputeShader);

            SetSize(width, height);
            
            SpeedFactor = speedFactor;
            LumVarianceFactor = lumVarianceFactor;
            ShadingRates = shadingRates;
        }

        public void Compute(Texture shaded, Texture velocity)
        {
            if (HAS_VARIABLE_RATE_SHADING)
            {
                GL.NV.BindShadingRateImage(Result.ID);
                GL.NV.ShadingRateImagePalette(0, 0, ShadingRates.Length, ref ShadingRates[0]);
            }

            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            shaded.BindToImageUnit(1, 0, false, 0, TextureAccess.ReadWrite, shaded.SizedInternalFormat);
            
            velocity.BindToUnit(0);

            shaderProgram.Use();
            GL.DispatchCompute((shaded.Width + TILE_SIZE - 1) / TILE_SIZE, (shaded.Height + TILE_SIZE - 1) / TILE_SIZE, 1);

            if (HAS_VARIABLE_RATE_SHADING)
            {
                GL.NV.ShadingRateImageBarrier(true);
            }
        }

        public void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Result.ImmutableAllocate(width / 16, height / 16, 1, SizedInternalFormat.R8ui);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
        }
    }
}
