﻿using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;

namespace IDKEngine.Render;

class LightingShadingRateClassifier : IDisposable
{
    // Defined by https://registry.khronos.org/OpenGL/extensions/NV/NV_shading_rate_image.txt
    public const int TILE_SIZE = 16;

    public static readonly bool IS_SUPPORTED = BBG.GetDeviceInfo().ExtensionSupport.VariableRateShading;

    // Keep in sync between shader and client code!
    public enum DebugMode : int
    {
        None,
        ShadingRate,
        Speed,
        Luminance,
        LuminanceVariance,
    }

    public record struct GpuSettings
    {
        public DebugMode DebugValue = DebugMode.None;
        public float SpeedFactor = 0.2f;
        public float LumVarianceFactor = 0.04f;

        public GpuSettings()
        {
        }
    }

    public GpuSettings Settings;
    public BBG.Rendering.ShadingRateNV[] ShadingRatePalette;

    public BBG.Texture Result;
    private BBG.Texture debugTexture;
    private readonly BBG.AbstractShaderProgram shaderProgram;
    private readonly BBG.AbstractShaderProgram debugProgram;
    public LightingShadingRateClassifier(Vector2i size, in GpuSettings settings)
    {
        shaderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "ShadingRateClassification/compute.glsl"));
        debugProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "ShadingRateClassification/debugCompute.glsl"));

        SetSize(size);

        ShadingRatePalette = [
            BBG.Rendering.ShadingRateNV._1InvocationPerPixel,
            BBG.Rendering.ShadingRateNV._1InvocationPer2x1Pixels,
            BBG.Rendering.ShadingRateNV._1InvocationPer2x2Pixels,
            BBG.Rendering.ShadingRateNV._1InvocationPer4x2Pixels,
            BBG.Rendering.ShadingRateNV._1InvocationPer4x4Pixels
        ];

        Settings = settings;
    }

    public void Compute(BBG.Texture shaded)
    {
        BBG.Computing.Compute("Generate Shading Rate Image", () =>
        {
            BBG.Cmd.SetUniforms(Settings);

            BBG.Cmd.BindImageUnit(Result, 0);
            BBG.Cmd.BindImageUnit(debugTexture, 1);
            BBG.Cmd.BindTextureUnit(shaded, 0);
            BBG.Cmd.UseShaderProgram(shaderProgram);

            BBG.Computing.Dispatch(MyMath.DivUp(shaded.Width, TILE_SIZE), MyMath.DivUp(shaded.Height, TILE_SIZE), 1);
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
        });
    }

    public void DebugRender(BBG.Texture dest)
    {
        if (Settings.DebugValue == DebugMode.None)
        {
            return;
        }

        BBG.Computing.Compute("Debug render shading rate attributes", () =>
        {
            BBG.Cmd.SetUniforms(Settings);

            BBG.Cmd.BindImageUnit(dest, 0);
            BBG.Cmd.BindTextureUnit(dest, 0);
            BBG.Cmd.BindTextureUnit(Settings.DebugValue == DebugMode.ShadingRate ? Result : debugTexture, 1);

            BBG.Cmd.UseShaderProgram(debugProgram);
            BBG.Computing.Dispatch(MyMath.DivUp(dest.Width, TILE_SIZE), MyMath.DivUp(dest.Height, TILE_SIZE), 1);
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
        });
    }

    public void SetSize(Vector2i size)
    {
        size.X = (int)MathF.Ceiling((float)size.X / TILE_SIZE);
        size.Y = (int)MathF.Ceiling((float)size.Y / TILE_SIZE);

        if (Result != null) Result.Dispose();
        Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
        Result.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
        Result.Allocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R8UInt);

        if (debugTexture != null) debugTexture.Dispose();
        debugTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
        debugTexture.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
        debugTexture.Allocate(Result.Width, Result.Height, 1, BBG.Texture.InternalFormat.R32Float);
    }

    public BBG.Rendering.VariableRateShadingNV GetRenderData()
    {
        return new BBG.Rendering.VariableRateShadingNV()
        {
            ShadingRateImage = Result,
            ShadingRatePalette = ShadingRatePalette,
        };
    }

    public void Dispose()
    {
        Result.Dispose();
        debugTexture.Dispose();
        shaderProgram.Dispose();
        debugProgram.Dispose();
    }
}
