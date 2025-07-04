﻿using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;

namespace IDKEngine.Render;

class VolumetricLighting : IDisposable
{
    public record struct GpuSettings
    {
        public Vector3 Absorbance = new Vector3(0.025f);
        public int SampleCount = 5;
        public float Scattering = 0.758f;
        public float MaxDist = 50.0f;
        public float Strength = 0.1f;

        public GpuSettings()
        {
        }
    }

    public Vector2i RenderResolution { get; private set; }
    public Vector2i PresentationResolution { get; private set; }

    private float _resolutionScale;
    public float ResolutionScale
    {
        get => _resolutionScale;

        set
        {
            _resolutionScale = value;
            SetSize(PresentationResolution);
        }
    }

    public GpuSettings Settings;

    public BBG.Texture Result;
    private BBG.Texture depthTexture;
    private BBG.Texture volumetricLightingTexture;

    private readonly BBG.AbstractShaderProgram volumetricLightingProgram;
    private readonly BBG.AbstractShaderProgram upscaleProgram;
    public unsafe VolumetricLighting(Vector2i size, in GpuSettings settings, float resolutionScale = 0.6f)
    {
        volumetricLightingProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VolumetricLight/compute.glsl"));
        upscaleProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VolumetricLight/Upscale/compute.glsl"));

        _resolutionScale = resolutionScale;
        SetSize(size);

        Settings = settings;
    }

    public void Compute()
    {
        BBG.Computing.Compute("Compute Volumetric Lighting", () =>
        {
            BBG.Cmd.SetUniforms(Settings);

            BBG.Cmd.BindImageUnit(volumetricLightingTexture, 0);
            BBG.Cmd.BindImageUnit(depthTexture, 1);
            BBG.Cmd.UseShaderProgram(volumetricLightingProgram);

            BBG.Computing.Dispatch(MyMath.DivUp(volumetricLightingTexture.Width, 8), MyMath.DivUp(volumetricLightingTexture.Height, 8), 1);
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
        });

        BBG.Computing.Compute("Upscale Volumetric Lighting", () =>
        {
            BBG.Cmd.BindImageUnit(Result, 0);
            BBG.Cmd.BindTextureUnit(volumetricLightingTexture, 0);
            BBG.Cmd.BindTextureUnit(depthTexture, 1);
            BBG.Cmd.UseShaderProgram(upscaleProgram);

            BBG.Computing.Dispatch(MyMath.DivUp(PresentationResolution.X, 8), MyMath.DivUp(PresentationResolution.Y, 8), 1);
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
        });
    }

    public void SetSize(Vector2i size)
    {
        PresentationResolution = new Vector2i(size.X, size.Y);
        RenderResolution = (Vector2i)((Vector2)PresentationResolution * ResolutionScale);

        if (Result != null) Result.Dispose();
        Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
        Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        Result.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        Result.Allocate(PresentationResolution.X, PresentationResolution.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

        if (volumetricLightingTexture != null) volumetricLightingTexture.Dispose();
        volumetricLightingTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
        volumetricLightingTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        volumetricLightingTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        volumetricLightingTexture.Allocate(RenderResolution.X, RenderResolution.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

        if (depthTexture != null) depthTexture.Dispose();
        depthTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
        depthTexture.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
        depthTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        depthTexture.Allocate(RenderResolution.X, RenderResolution.Y, 1, BBG.Texture.InternalFormat.R32Float);
    }

    public void Dispose()
    {
        Result.Dispose();
        volumetricLightingTexture.Dispose();
        depthTexture.Dispose();

        volumetricLightingProgram.Dispose();
        upscaleProgram.Dispose();
    }
}
