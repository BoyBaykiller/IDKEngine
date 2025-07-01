using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;

namespace IDKEngine.Render;

class TAAResolve : IDisposable
{
    public record struct GpuSettings
    {
        public bool IsNaiveTaa;
        public float PreferAliasingOverBlur = 0.25f;

        public GpuSettings()
        {
        }
    }

    public GpuSettings Settings;

    public BBG.Texture Result => (frame % 2 == 0) ? taaPing : taaPong;
    public BBG.Texture PrevResult => (frame % 2 == 0) ? taaPong : taaPing;

    private BBG.Texture taaPing;
    private BBG.Texture taaPong;
    private readonly BBG.AbstractShaderProgram taaResolveProgram;
    private int frame;
    public TAAResolve(Vector2i size, in GpuSettings settings)
    {
        taaResolveProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "TAAResolve/compute.glsl"));

        SetSize(size);
        Settings = settings;
    }

    public void Compute(BBG.Texture color)
    {
        frame++;

        BBG.Computing.Compute("Temporal Anti Aliasing", () =>
        {
            BBG.Cmd.SetUniforms(Settings);

            BBG.Cmd.BindImageUnit(Result, 0);
            BBG.Cmd.BindTextureUnit(PrevResult, 0);
            BBG.Cmd.BindTextureUnit(color, 1);
            BBG.Cmd.UseShaderProgram(taaResolveProgram);

            BBG.Computing.Dispatch(MyMath.DivUp(Result.Width, 8), MyMath.DivUp(Result.Height, 8), 1);
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
        });
    }

    public void SetSize(Vector2i size)
    {
        if (taaPing != null) taaPing.Dispose();
        taaPing = new BBG.Texture(BBG.Texture.Type.Texture2D);
        taaPing.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        taaPing.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        taaPing.Allocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

        if (taaPong != null) taaPong.Dispose();
        taaPong = new BBG.Texture(BBG.Texture.Type.Texture2D);
        taaPong.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        taaPong.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        taaPong.Allocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);
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
