using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class Voxelizer : IDisposable
    {
        public static readonly bool ALLOW_CONSERVATIVE_RASTER      = BBG.GetDeviceInfo().ExtensionSupport.ConservativeRaster;

        public static readonly bool TAKE_ATOMIC_FP16_PATH          = BBG.GetDeviceInfo().ExtensionSupport.AtomicFp16Vector;

        public static readonly bool TAKE_FAST_GEOMETRY_SHADER_PATH = BBG.GetDeviceInfo().ExtensionSupport.GeometryShaderPassthrough &&
                                                                     BBG.GetDeviceInfo().ExtensionSupport.ViewportSwizzle;

        public Vector3 GridMin
        {
            get => gpuVoxelizerData.GridMin;

            set
            {
                gpuVoxelizerData.GridMin = Vector3.ComponentMin(value, gpuVoxelizerData.GridMax - new Vector3(0.1f));
            }
        }
        public Vector3 GridMax
        {
            get => gpuVoxelizerData.GridMax;

            set
            {
                gpuVoxelizerData.GridMax = Vector3.ComponentMax(value, gpuVoxelizerData.GridMin + new Vector3(0.1f));
            }
        }

        public float DebugStepMultiplier;
        public float DebugConeAngle;

        private bool _isConservativeRasterization;
        public bool IsConservativeRasterization
        {
            get => _isConservativeRasterization;

            set
            {
                _isConservativeRasterization = value;

                if (_isConservativeRasterization && !ALLOW_CONSERVATIVE_RASTER)
                {
                    Logger.Log(Logger.LogLevel.Error, $"{nameof(ALLOW_CONSERVATIVE_RASTER)} was {ALLOW_CONSERVATIVE_RASTER}. Conservative rasterization requires GL_NV_conservative_raster");
                    _isConservativeRasterization = false;
                }
            }
        }

        public BBG.Texture ResultVoxels;
        private readonly BBG.Texture[] intermediateResultsRbg; // only used if no support for GL_NV_shader_atomic_fp16_vector
        private readonly BBG.AbstractShaderProgram mergeIntermediatesProgram; // only used if no support for GL_NV_shader_atomic_fp16_vector
        private readonly BBG.AbstractShaderProgram clearTexturesProgram;
        private readonly BBG.AbstractShaderProgram voxelizeProgram;
        private readonly BBG.AbstractShaderProgram mipmapProgram;
        private readonly BBG.AbstractShaderProgram visualizeDebugProgram;
        public readonly BBG.TypedBuffer<GpuVoxelizerData> voxelizerDataBuffer;
        private GpuVoxelizerData gpuVoxelizerData;

        public Voxelizer(int width, int height, int depth, Vector3 gridMin, Vector3 gridMax, float debugConeAngle = 0.0f, float debugStepMultiplier = 0.4f)
        {
            BBG.AbstractShaderProgram.SetShaderInsertionValue(nameof(TAKE_ATOMIC_FP16_PATH), TAKE_ATOMIC_FP16_PATH);
            BBG.AbstractShaderProgram.SetShaderInsertionValue(nameof(TAKE_FAST_GEOMETRY_SHADER_PATH), TAKE_FAST_GEOMETRY_SHADER_PATH);

            {
                List<BBG.AbstractShader> voxelizeProgramShaders = [
                    BBG.AbstractShader.FromFile(BBG.ShaderStage.Vertex, "VXGI/Voxelize/Voxelize/vertex.glsl"),
                    BBG.AbstractShader.FromFile(BBG.ShaderStage.Fragment, "VXGI/Voxelize/Voxelize/fragment.glsl")
                ];
                if (TAKE_FAST_GEOMETRY_SHADER_PATH)
                {
                    voxelizeProgramShaders.Add(BBG.AbstractShader.FromFile(BBG.ShaderStage.Geometry, "VXGI/Voxelize/Voxelize/geometry.glsl"));
                }

                voxelizeProgram = new BBG.AbstractShaderProgram(voxelizeProgramShaders.ToArray());

                clearTexturesProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VXGI/Voxelize/Clear/compute.glsl"));
            }

            mipmapProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VXGI/Voxelize/Mipmap/compute.glsl"));
            visualizeDebugProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VXGI/Voxelize/DebugVisualization/compute.glsl"));
            if (!TAKE_ATOMIC_FP16_PATH)
            {
                intermediateResultsRbg = new BBG.Texture[3];
                mergeIntermediatesProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "VXGI/Voxelize/MergeIntermediates/compute.glsl"));
            }

            voxelizerDataBuffer = new BBG.TypedBuffer<GpuVoxelizerData>();
            voxelizerDataBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);
            voxelizerDataBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 5);

            SetSize(width, height, depth);

            gpuVoxelizerData = new GpuVoxelizerData();
            GridMin = gridMin;
            GridMax = gridMax;
            DebugConeAngle = debugConeAngle;
            DebugStepMultiplier = debugStepMultiplier;
        }

        public void Render(ModelManager modelManager)
        {
            ClearTextures();
            Voxelize(modelManager);
            Mipmap();
        }

        private void ClearTextures()
        {
            BBG.Computing.Compute("Clear textures", () =>
            {
                BBG.Cmd.BindImageUnit(ResultVoxels, 0, 0, true);
                if (!TAKE_ATOMIC_FP16_PATH)
                {
                    BBG.Cmd.BindImageUnit(intermediateResultsRbg[0], 1, 0, true);
                    BBG.Cmd.BindImageUnit(intermediateResultsRbg[1], 2, 0, true);
                    BBG.Cmd.BindImageUnit(intermediateResultsRbg[2], 3, 0, true);
                }

                BBG.Cmd.UseShaderProgram(clearTexturesProgram);
                BBG.Computing.Dispatch((ResultVoxels.Width + 4 - 1) / 4, (ResultVoxels.Height + 4 - 1) / 4, (ResultVoxels.Depth + 4 - 1) / 4);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderImageAccessBarrierBit);
            });
        }

        private void Voxelize(ModelManager modelManager)
        {
            BBG.Rendering.Render("Voxelize", new BBG.Rendering.NoRenderAttachmentsParams()
            {
                Width = ResultVoxels.Width,
                Height = ResultVoxels.Height,
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                EnabledCapabilities = [BBG.Rendering.CapIf(IsConservativeRasterization, BBG.Rendering.Capability.ConservativeRasterizationNV)],
                DepthConvention = BBG.Rendering.DepthConvention.NegativeOneToOne,
            }, () =>
            {
                voxelizerDataBuffer.UploadElements(gpuVoxelizerData);

                if (TAKE_FAST_GEOMETRY_SHADER_PATH)
                {
                    Span<BBG.Rendering.Viewport> viewports = [
                        new BBG.Rendering.Viewport() {
                            Size = new Vector2(ResultVoxels.Width, ResultVoxels.Height),
                        },
                        new BBG.Rendering.Viewport() {
                            Size = new Vector2(ResultVoxels.Width, ResultVoxels.Height),
                            ViewportSwizzle = new BBG.Rendering.ViewportSwizzleNV() { Y = BBG.Rendering.ViewportSwizzleAxisNV.PositiveZ, Z = BBG.Rendering.ViewportSwizzleAxisNV.PositiveY },
                        },
                        new BBG.Rendering.Viewport() {
                            Size = new Vector2(ResultVoxels.Width, ResultVoxels.Height),
                            ViewportSwizzle = new BBG.Rendering.ViewportSwizzleNV() { X = BBG.Rendering.ViewportSwizzleAxisNV.PositiveZ, Z = BBG.Rendering.ViewportSwizzleAxisNV.PositiveX },
                        }
                    ];
                    BBG.Rendering.SetViewports(viewports);
                }

                BBG.Cmd.BindImageUnit(ResultVoxels, 0, 0, true);
                if (!TAKE_ATOMIC_FP16_PATH)
                {
                    BBG.Cmd.BindImageUnit(intermediateResultsRbg[0], BBG.Texture.InternalFormat.R32Uint, 1, 0, true);
                    BBG.Cmd.BindImageUnit(intermediateResultsRbg[1], BBG.Texture.InternalFormat.R32Uint, 2, 0, true);
                    BBG.Cmd.BindImageUnit(intermediateResultsRbg[2], BBG.Texture.InternalFormat.R32Uint, 3, 0, true);
                }

                BBG.Cmd.UseShaderProgram(voxelizeProgram);

                BBG.Rendering.InferViewportSize();
                if (TAKE_FAST_GEOMETRY_SHADER_PATH)
                {
                    modelManager.Draw();
                }
                else
                {
                    // Instead of doing a single draw call with a standard geometry shader to select the swizzle
                    // we render the scene 3 times, each time with a different swizzle. I have observed this to be slightly faster
                    for (int i = 0; i < 3; i++)
                    {
                        voxelizeProgram.Upload(0, i);
                        modelManager.Draw();
                    }
                }
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderImageAccessBarrierBit | BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });

            if (!TAKE_ATOMIC_FP16_PATH)
            {
                BBG.Computing.Compute("Merge intermediate textures", () =>
                {
                    BBG.Cmd.BindImageUnit(ResultVoxels, 0, 0, true);
                    BBG.Cmd.BindTextureUnit(intermediateResultsRbg[0], 0);
                    BBG.Cmd.BindTextureUnit(intermediateResultsRbg[1], 1);
                    BBG.Cmd.BindTextureUnit(intermediateResultsRbg[2], 2);
                    BBG.Cmd.UseShaderProgram(mergeIntermediatesProgram);

                    BBG.Computing.Dispatch((ResultVoxels.Width + 4 - 1) / 4, (ResultVoxels.Height + 4 - 1) / 4, (ResultVoxels.Depth + 4 - 1) / 4);
                    BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderImageAccessBarrierBit | BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
                });
            }
        }

        private void Mipmap()
        {
            BBG.Cmd.BindTextureUnit(ResultVoxels, 0);
            BBG.Cmd.UseShaderProgram(mipmapProgram);

            for (int i = 1; i < ResultVoxels.Levels; i++)
            {
                BBG.Computing.Compute($"Downsample Voxel texture to level {i}", () =>
                {
                    mipmapProgram.Upload(0, i - 1);

                    Vector3i size = BBG.Texture.GetMipmapLevelSize(ResultVoxels.Width, ResultVoxels.Height, ResultVoxels.Depth, i);

                    BBG.Cmd.BindImageUnit(ResultVoxels, 0, i, true);
                    BBG.Computing.Dispatch((size.X + 4 - 1) / 4, (size.Y + 4 - 1) / 4, (size.Z + 4 - 1) / 4);
                    BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderImageAccessBarrierBit | BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
                });
            }
        }

        public void DebugRender(BBG.Texture debugResult)
        {
            BBG.Computing.Compute("Visualize Voxel texture", () =>
            {
                visualizeDebugProgram.Upload(0, DebugStepMultiplier);
                visualizeDebugProgram.Upload(1, DebugConeAngle);

                BBG.Cmd.BindImageUnit(debugResult, 0);
                BBG.Cmd.BindTextureUnit(ResultVoxels, 0);
                BBG.Cmd.UseShaderProgram(visualizeDebugProgram);
                BBG.Computing.Dispatch((debugResult.Width + 8 - 1) / 8, (debugResult.Height + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });

        }

        public void SetSize(int width, int height, int depth)
        {
            if (ResultVoxels != null) ResultVoxels.Dispose();
            ResultVoxels = new BBG.Texture(BBG.Texture.Type.Texture3D);
            ResultVoxels.SetFilter(BBG.Sampler.MinFilter.LinearMipmapLinear, BBG.Sampler.MagFilter.Linear);
            ResultVoxels.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            ResultVoxels.SetAnisotropy(BBG.Sampler.Anisotropy.Samples16x);
            ResultVoxels.Allocate(width, height, depth, BBG.Texture.InternalFormat.R16G16B16A16Float, BBG.Texture.GetMaxMipmapLevel(width, height, depth));

            if (!TAKE_ATOMIC_FP16_PATH)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (intermediateResultsRbg[i] != null) intermediateResultsRbg[i].Dispose();
                    intermediateResultsRbg[i] = new BBG.Texture(BBG.Texture.Type.Texture3D);
                    intermediateResultsRbg[i].SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
                    intermediateResultsRbg[i].Allocate(width, height, depth, BBG.Texture.InternalFormat.R32Float);
                }
            }
        }

        public void Dispose()
        {
            ResultVoxels.Dispose();
            if (!TAKE_ATOMIC_FP16_PATH)
            {
                for (int i = 0; i < 3; i++)
                {
                    intermediateResultsRbg[i].Dispose();
                }
                mergeIntermediatesProgram.Dispose();
            }

            clearTexturesProgram.Dispose();
            voxelizeProgram.Dispose();
            mipmapProgram.Dispose();
            visualizeDebugProgram.Dispose();

            voxelizerDataBuffer.Dispose();
        }
    }
}
