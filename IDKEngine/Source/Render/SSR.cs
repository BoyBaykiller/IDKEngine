using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;

namespace IDKEngine.Render;

class SSR : IDisposable
{
    public record struct GpuSettings
    {
        public int SampleCount = 30;
        public int BinarySearchCount = 8;
        public float MaxDist = 50.0f;

        public GpuSettings()
        {
        }
    }

    public GpuSettings Settings;


    public BBG.Texture Result;
    private readonly BBG.AbstractShaderProgram shaderProgram;
    public SSR(Vector2i size, in GpuSettings settings)
    {
        shaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "SSR/compute.glsl"));

        SetSize(size);

        Settings = settings;
    }

    public void Compute(BBG.Texture colorTexture)
    {
        BBG.Computing.Compute("Compute SSR", () =>
        {
            BBG.Cmd.SetUniforms(Settings);

            BBG.Cmd.BindImageUnit(Result, 0);
            BBG.Cmd.BindTextureUnit(colorTexture, 0);
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
        Result.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        Result.Allocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);
    }

    public void Dispose()
    {
        Result.Dispose();
        shaderProgram.Dispose();
    }
}
