using System;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.Render
{
    class TAAResolve : IDisposable
    {
        public bool IsNaiveTaa;
        public float PreferAliasingOverBlur;

        public BBG.Texture Result => (frame % 2 == 0) ? taaPing : taaPong;
        public BBG.Texture PrevResult => (frame % 2 == 0) ? taaPong : taaPing;

        private BBG.Texture taaPing;
        private BBG.Texture taaPong;
        private readonly BBG.AbstractShaderProgram taaResolveProgram;
        private int frame;
        public TAAResolve(Vector2i size, float preferAliasingOverBlurAmount = 0.25f)
        {
            taaResolveProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "TAAResolve/compute.glsl"));

            SetSize(size);

            PreferAliasingOverBlur = preferAliasingOverBlurAmount;
        }

        public void Compute(BBG.Texture color)
        {
            frame++;

            BBG.Computing.Compute("Temporal Anti Aliasing", () =>
            {
                taaResolveProgram.Upload("PreferAliasingOverBlur", PreferAliasingOverBlur);
                taaResolveProgram.Upload("IsNaiveTaa", IsNaiveTaa);

                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.BindTextureUnit(PrevResult, 0);
                BBG.Cmd.BindTextureUnit(color, 1);
                BBG.Cmd.UseShaderProgram(taaResolveProgram);

                BBG.Computing.Dispatch((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });
        }

        public void SetSize(Vector2i size)
        {
            if (taaPing != null) taaPing.Dispose();
            taaPing = new BBG.Texture(BBG.Texture.Type.Texture2D);
            taaPing.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            taaPing.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            taaPing.ImmutableAllocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

            if (taaPong != null) taaPong.Dispose();
            taaPong = new BBG.Texture(BBG.Texture.Type.Texture2D);
            taaPong.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            taaPong.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            taaPong.ImmutableAllocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);
        }

        public void Dispose()
        {
            taaPing.Dispose();
            taaPong.Dispose();
            taaResolveProgram.Dispose();
        }

        public static float GetRecommendedMipmapBias(int renderWidth, int displayWith)
        {
            return MathF.Log2((float)renderWidth / displayWith) - 1.0f;
        }
    }
}
