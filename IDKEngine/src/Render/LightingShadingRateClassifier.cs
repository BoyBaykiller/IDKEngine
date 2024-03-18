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
            NoDebug,
            ShadingRate,
            Speed,
            Luminance,
            LuminanceVariance,
        }

        public struct GpuSettings
        {
            public DebugMode DebugValue;
            public float SpeedFactor;
            public float LumVarianceFactor;

            public static GpuSettings Default = new GpuSettings()
            {
                DebugValue = DebugMode.NoDebug,
                SpeedFactor = 0.2f,
                LumVarianceFactor = 0.025f,
            };
        }

        public GpuSettings Settings;

        private Texture debugTexture;
        private readonly ShaderProgram shaderProgram;
        private readonly ShaderProgram debugProgram;
        private readonly TypedBuffer<GpuSettings> bufferGpuSettings;
        public LightingShadingRateClassifier(int width, int height, in GpuSettings settings)
            : base(width, height, new NvShadingRateImage[]
            {
                NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
            })
        {
            shaderProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "ShadingRateClassification/compute.glsl"));
            debugProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "ShadingRateClassification/debugCompute.glsl"));

            bufferGpuSettings = new TypedBuffer<GpuSettings>();
            bufferGpuSettings.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(width, height);

            Settings = settings;
        }

        public void Compute(Texture shaded)
        {
            bufferGpuSettings.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            bufferGpuSettings.UploadElements(Settings);

            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            debugTexture.BindToImageUnit(1, 0, false, 0, TextureAccess.WriteOnly, debugTexture.SizedInternalFormat);
            shaded.BindToUnit(0);

            shaderProgram.Use();
            GL.DispatchCompute((shaded.Width + TILE_SIZE - 1) / TILE_SIZE, (shaded.Height + TILE_SIZE - 1) / TILE_SIZE, 1);
        }

        public void DebugRender(Texture dest)
        {
            if (Settings.DebugValue == DebugMode.NoDebug)
            {
                return;
            }

            bufferGpuSettings.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            bufferGpuSettings.UploadElements(Settings);

            dest.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, dest.SizedInternalFormat);
            dest.BindToUnit(0);
            if (Settings.DebugValue != DebugMode.ShadingRate)
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
            bufferGpuSettings.Dispose();
        }
    }
}
