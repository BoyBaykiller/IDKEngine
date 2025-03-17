using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;

namespace IDKEngine.Render
{
    class ConeTracer : IDisposable
    {
        public record struct GpuSettings
        {
            public int MaxSamples = 4;
            public float StepMultiplier = 0.16f;
            public float GIBoost = 1.3f;
            public float GISkyBoxBoost = 1.0f / 1.3f;
            public float NormalRayOffset = 1.0f;
            public bool IsTemporalAccumulation = true;

            public GpuSettings()
            {
            }
        }

        public GpuSettings Settings;

        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram shaderProgram;
        public ConeTracer(Vector2i size, in GpuSettings settings)
        {
            shaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VXGI/ConeTraceGI/compute.glsl"));

            SetSize(size);

            Settings = settings;
        }

        public void Compute(BBG.Texture voxelsAlbedo)
        {
            BBG.Computing.Compute("Cone Trace GI", () =>
            {
                BBG.Cmd.SetUniforms(Settings);

                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.BindTextureUnit(voxelsAlbedo, 0);
                BBG.Cmd.UseShaderProgram(shaderProgram);

                BBG.Computing.Dispatch(MyMath.DivUp(Result.Width, 8), MyMath.DivUp(Result.Height, 8), 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });
        }

        public void SetSize(Vector2i size)
        {
            if (Result != null) Result.Dispose();
            Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
            Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            Result.Allocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);
        }

        public void Dispose()
        {
            Result.Dispose();
            shaderProgram.Dispose();
        }
    }
}
