using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class LightingShadingRateClassifier : VariableRateShading, IDisposable
    {
        // used in shader and client code - keep in sync!
        public enum DebugMode
        {
            NoDebug = 0,
            ShadingRate = 1,
            Speed = 2,
            Luminance = 3,
            LuminanceVariance = 4,
        }

        private DebugMode _debugValue;
        public DebugMode DebugValue
        {
            get => _debugValue;

            set
            {
                _debugValue = value;
                shaderProgram.Upload("DebugMode", (int)_debugValue);
                debugProgram.Upload("DebugMode", (int)_debugValue);
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

        private Texture debugTexture;
        private readonly ShaderProgram shaderProgram;
        private readonly ShaderProgram debugProgram;
        public LightingShadingRateClassifier(int width, int height, float lumVarianceFactor, float speedFactor)
            : base(width, height, new NvShadingRateImage[]
            {
                NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
            })
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl")));
            debugProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/debugCompute.glsl")));

            SetSize(width, height);

            LumVarianceFactor = lumVarianceFactor;
            SpeedFactor = speedFactor;
        }

        public void Compute(Texture shaded)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            debugTexture.BindToImageUnit(1, 0, false, 0, TextureAccess.WriteOnly, debugTexture.SizedInternalFormat);
            shaded.BindToUnit(0);

            shaderProgram.Use();
            GL.DispatchCompute((shaded.Width + TILE_SIZE - 1) / TILE_SIZE, (shaded.Height + TILE_SIZE - 1) / TILE_SIZE, 1);
        }

        public void DebugRender(Texture dest)
        {
            if (DebugValue == DebugMode.NoDebug)
            {
                return;
            }

            dest.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, dest.SizedInternalFormat);
            dest.BindToUnit(0);
            if (DebugValue != DebugMode.ShadingRate)
            {
                debugTexture.BindToUnit(1);
            }
            else
            {
                Result.BindToUnit(1);
            }

            debugProgram.Use();
            GL.DispatchCompute((dest.Width + TILE_SIZE - 1) / TILE_SIZE, (dest.Height + TILE_SIZE - 1) / TILE_SIZE, 1);
        }

        public new void SetSize(int width, int height)
        {
            base.SetSize(width, height);

            if (debugTexture != null) debugTexture.Dispose();
            debugTexture = new Texture(TextureTarget2d.Texture2D);
            debugTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            debugTexture.ImmutableAllocate(base.Result.Width, base.Result.Height, 1, SizedInternalFormat.R16f);
        }

        public new void Dispose()
        {
            base.Dispose();

            debugTexture.Dispose();
            shaderProgram.Dispose();
            debugProgram.Dispose();
        }
    }
}
