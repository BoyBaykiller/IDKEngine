using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;

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
        private readonly AbstractShaderProgram shaderProgram;
        private readonly AbstractShaderProgram debugProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public LightingShadingRateClassifier(Vector2i size, in GpuSettings settings)
            : base(size, new NvShadingRateImage[]
            {
                NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
            })
        {
            shaderProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "ShadingRateClassification/compute.glsl"));
            debugProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "ShadingRateClassification/debugCompute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);

            SetSize(size);

            Settings = settings;
        }

        public void Compute(Texture shaded)
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            Result.BindToImageUnit(0, Result.TextureFormat);
            debugTexture.BindToImageUnit(1, debugTexture.TextureFormat);
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

            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(Settings);

            dest.BindToImageUnit(0, dest.TextureFormat);
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

        public new void SetSize(Vector2i size)
        {
            base.SetSize(size);

            if (debugTexture != null) debugTexture.Dispose();
            debugTexture = new Texture(Texture.Type.Texture2D);
            debugTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            debugTexture.ImmutableAllocate(base.Result.Width, base.Result.Height, 1, Texture.InternalFormat.R16Float);
        }

        public new void Dispose()
        {
            base.Dispose();

            debugTexture.Dispose();
            shaderProgram.Dispose();
            debugProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }
    }
}
