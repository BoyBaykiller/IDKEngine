using System;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.Render
{
    class LightingShadingRateClassifier : IDisposable
    {
        // Defined by https://registry.khronos.org/OpenGL/extensions/NV/NV_shading_rate_image.txt
        public const int TILE_SIZE = 16;

        public static bool IS_SUPPORTED = BBG.GetDeviceInfo().ExtensionSupport.VariableRateShading;

        // Used in shader and client code - keep in sync!
        public enum DebugMode : int
        {
            None,
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
                DebugValue = DebugMode.None,
                SpeedFactor = 0.2f,
                LumVarianceFactor = 0.04f,
            };
        }

        public GpuSettings Settings;
        public BBG.Rendering.ShadingRate[] ShadingRatePalette;

        public BBG.Texture Result;
        private BBG.Texture debugTexture;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        private readonly BBG.AbstractShaderProgram debugProgram;
        private readonly BBG.TypedBuffer<GpuSettings> gpuSettingsBuffer;
        public unsafe LightingShadingRateClassifier(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderType.Compute, "ShadingRateClassification/compute.glsl"));
            debugProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderType.Compute, "ShadingRateClassification/debugCompute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            ShadingRatePalette = [
                BBG.Rendering.ShadingRate._1InvocationPerPixelNV,
                BBG.Rendering.ShadingRate._1InvocationPer2x1PixelsNV,
                BBG.Rendering.ShadingRate._1InvocationPer2x2PixelsNV,
                BBG.Rendering.ShadingRate._1InvocationPer4x2PixelsNV,
                BBG.Rendering.ShadingRate._1InvocationPer4x4PixelsNV
            ];

            Settings = settings;
        }

        public void Compute(BBG.Texture shaded)
        {
            BBG.Computing.Compute("Generate Shading Rate Image", () =>
            {
                gpuSettingsBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 7);
                gpuSettingsBuffer.UploadElements(Settings);

                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.BindImageUnit(debugTexture, 1);
                BBG.Cmd.BindTextureUnit(shaded, 0);
                BBG.Cmd.UseShaderProgram(shaderProgram);

                BBG.Computing.Dispatch((shaded.Width + TILE_SIZE - 1) / TILE_SIZE, (shaded.Height + TILE_SIZE - 1) / TILE_SIZE, 1);
            });
        }

        public void DebugRender(BBG.Texture dest)
        {
            if (Settings.DebugValue == DebugMode.None)
            {
                return;
            }

            BBG.Computing.Compute("Debug shading sate attributes", () =>
            {
                gpuSettingsBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 7);
                gpuSettingsBuffer.UploadElements(Settings);

                BBG.Cmd.BindImageUnit(dest, 0);
                BBG.Cmd.BindTextureUnit(dest, 0);
                BBG.Cmd.BindTextureUnit(Settings.DebugValue == DebugMode.ShadingRate ? Result : debugTexture, 1);

                BBG.Cmd.UseShaderProgram(debugProgram);
                BBG.Computing.Dispatch((dest.Width + TILE_SIZE - 1) / TILE_SIZE, (dest.Height + TILE_SIZE - 1) / TILE_SIZE, 1);
            });
        }

        public void SetSize(Vector2i size)
        {
            size.X = (int)MathF.Ceiling((float)size.X / TILE_SIZE);
            size.Y = (int)MathF.Ceiling((float)size.Y / TILE_SIZE);

            if (Result != null) Result.Dispose();
            Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
            Result.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
            Result.ImmutableAllocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R8Uint);

            if (debugTexture != null) debugTexture.Dispose();
            debugTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            debugTexture.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
            debugTexture.ImmutableAllocate(Result.Width, Result.Height, 1, BBG.Texture.InternalFormat.R32Float);
        }

        public void Dispose()
        {
            debugTexture.Dispose();
            shaderProgram.Dispose();
            debugProgram.Dispose();
            gpuSettingsBuffer.Dispose();
        }

        public BBG.Rendering.VariableRateShadingNV GetVariableRateShading()
        {
            return new BBG.Rendering.VariableRateShadingNV()
            {
                ShadingRateImage = Result,
                ShadingRatePalette = ShadingRatePalette,
            };
        }
    }
}
