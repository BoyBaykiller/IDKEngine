using System;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class RasterPipeline : IDisposable
    {
        private bool _takeMeshShaderPathCamera;
        public bool TakeMeshShaderPath
        {
            get => _takeMeshShaderPathCamera;

            set
            {
                _takeMeshShaderPathCamera = value;

                if (_takeMeshShaderPathCamera && !BBG.GetDeviceInfo().ExtensionSupport.MeshShader)
                {
                    Logger.Log(Logger.LogLevel.Error, $"Mesh shader path requires GL_NV_mesh_shader");
                    _takeMeshShaderPathCamera = false;
                }

                if (gBufferProgram != null) gBufferProgram.Dispose();
                BBG.AbstractShaderProgram.SetShaderInsertionValue("TAKE_MESH_SHADER_PATH_CAMERA", TakeMeshShaderPath);

                if (TakeMeshShaderPath)
                {
                    gBufferProgram = new BBG.AbstractShaderProgram(
                       new BBG.AbstractShader(BBG.ShaderStage.TaskNV, "GBuffer/MeshPath/task.glsl"),
                       new BBG.AbstractShader(BBG.ShaderStage.MeshNV, "GBuffer/MeshPath/mesh.glsl"),
                       new BBG.AbstractShader(BBG.ShaderStage.Fragment, "GBuffer/fragment.glsl"));
                }
                else
                {
                    gBufferProgram = new BBG.AbstractShaderProgram(
                        new BBG.AbstractShader(BBG.ShaderStage.Vertex, "GBuffer/VertexPath/vertex.glsl"),
                        new BBG.AbstractShader(BBG.ShaderStage.Fragment, "GBuffer/fragment.glsl"));
                }
            }
        }

        private bool _isHiZCulling;
        public bool IsHiZCulling
        {
            get => _isHiZCulling;

            set
            {
                _isHiZCulling = value;
                BBG.AbstractShaderProgram.SetShaderInsertionValue("IS_HI_Z_CULLING", IsHiZCulling);
            }
        }

        public Vector2i RenderResolution { get; private set; }
        public Vector2i PresentationResolution { get; private set; }

        public enum TemporalAntiAliasingMode : int
        {
            None,
            TAA,
            FSR2,
        }

        private TemporalAntiAliasingMode _temporalAntiAliasingMode;
        public TemporalAntiAliasingMode TemporalAntiAliasing
        {
            get => _temporalAntiAliasingMode;

            set
            {
                if (!FSR2Wrapper.IS_SUPPORTED && value == TemporalAntiAliasingMode.FSR2)
                {
                    Logger.Log(Logger.LogLevel.Error, $"{TemporalAntiAliasingMode.FSR2} is Windows only");
                    return;
                }

                _temporalAntiAliasingMode = value;

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA && TaaResolve == null)
                {
                    TaaResolve = new TAAResolve(PresentationResolution);
                }
                else
                {
                    if (TaaResolve != null) { TaaResolve.Dispose(); TaaResolve = null; }
                }

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2 && FSR2Wrapper == null)
                {
                    FSR2Wrapper = new FSR2Wrapper(RenderResolution, PresentationResolution);
                }
                else
                {
                    if (FSR2Wrapper != null) { FSR2Wrapper.Dispose(); FSR2Wrapper = null; }
                }
            }
        }

        public enum ShadowTechnique
        {
            None,
            PcfShadowMap,
            RayTraced
        }

        public ShadowTechnique ShadowMode;

        public bool IsWireframe;
        public bool IsVXGI;
        public bool IsSSAO;
        public bool IsSSR;
        public bool IsVariableRateShading;
        public bool GenerateShadowMaps;

        public bool IsConfigureGridMode;
        public bool GridReVoxelize;
        public bool GridFollowCamera;

        public int RayTracingSamples;

        public int TAASamples;
        public float FSR2AdditionalMipBias;

        // Runs at render presentation resolution
        public TAAResolve TaaResolve;
        public FSR2Wrapper FSR2Wrapper;

        // Runs at render resolution
        public readonly SSAO SSAO;
        public readonly SSR SSR;
        public readonly LightingShadingRateClassifier LightingVRS;
        public readonly ConeTracer ConeTracer;

        public readonly Voxelizer Voxelizer;

        public BBG.Texture Result
        {
            get
            {
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
                {
                    return TaaResolve.Result;
                }

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2)
                {
                    return FSR2Wrapper.Result;
                }

                return resultBeforeTAA;
            }
        }

        private BBG.Texture resultBeforeTAA;

        public BBG.Texture AlbedoAlphaTexture;
        public BBG.Texture NormalTexture;
        public BBG.Texture MetallicRoughnessTexture;
        public BBG.Texture EmissiveTexture;
        public BBG.Texture VelocityTexture;
        public BBG.Texture DepthTexture;

        private BBG.AbstractShaderProgram gBufferProgram;
        private readonly BBG.AbstractShaderProgram deferredLightingProgram;
        private readonly BBG.AbstractShaderProgram skyBoxProgram;
        private readonly BBG.AbstractShaderProgram mergeLightingProgram;
        private readonly BBG.AbstractShaderProgram hiZGenerateProgram;
        private readonly BBG.AbstractShaderProgram cullingProgram;

        private readonly BBG.TypedBuffer<GpuTaaData> taaDataBuffer;
        private GpuTaaData gpuTaaData;

        private readonly BBG.TypedBuffer<GpuGBuffer> gBufferData;
        private GpuGBuffer gpuGBufferData;

        private int frameIndex;
        public unsafe RasterPipeline(Vector2i renderSize, Vector2i renderPresentationSize)
        {
            TakeMeshShaderPath = false;
            IsHiZCulling = false;

            deferredLightingProgram = new BBG.AbstractShaderProgram(
                new BBG.AbstractShader(BBG.ShaderStage.Vertex, "ToScreen/vertex.glsl"),
                new BBG.AbstractShader(BBG.ShaderStage.Fragment, "DeferredLighting/fragment.glsl"));

            skyBoxProgram = new BBG.AbstractShaderProgram(
                new BBG.AbstractShader(BBG.ShaderStage.Vertex, "SkyBox/vertex.glsl"),
                new BBG.AbstractShader(BBG.ShaderStage.Fragment, "SkyBox/fragment.glsl"));

            hiZGenerateProgram = new BBG.AbstractShaderProgram(
                new BBG.AbstractShader(BBG.ShaderStage.Vertex, "ToScreen/vertex.glsl"),
                new BBG.AbstractShader(BBG.ShaderStage.Fragment, "MeshCulling/Camera/HiZGenerate/fragment.glsl"));

            cullingProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderStage.Compute, "MeshCulling/Camera/Cull/compute.glsl"));

            mergeLightingProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderStage.Compute, "MergeTextures/compute.glsl"));

            taaDataBuffer = new BBG.TypedBuffer<GpuTaaData>();
            taaDataBuffer.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, 1);
            taaDataBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 3);

            gBufferData = new BBG.TypedBuffer<GpuGBuffer>();
            gBufferData.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, 1);
            gBufferData.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 6);

            SSAO = new SSAO(renderSize, SSAO.GpuSettings.Default);
            SSR = new SSR(renderSize, SSR.GpuSettings.Default);
            LightingVRS = new LightingShadingRateClassifier(renderSize, LightingShadingRateClassifier.GpuSettings.Default);
            Voxelizer = new Voxelizer(256, 256, 256, new Vector3(-28.0f, -3.0f, -17.0f), new Vector3(28.0f, 20.0f, 17.0f));
            ConeTracer = new ConeTracer(renderSize, ConeTracer.GpuSettings.Default);

            IsWireframe = false;
            IsSSAO = true;
            IsSSR = false;
            IsVariableRateShading = false;
            IsVXGI = false;
            GenerateShadowMaps = true;
            GridReVoxelize = true;

            RayTracingSamples = 1;
            ShadowMode = ShadowTechnique.PcfShadowMap;

            SetSize(renderSize, renderPresentationSize);

            TAASamples = 6;
            FSR2AdditionalMipBias = 0.25f;
            TemporalAntiAliasing = TemporalAntiAliasingMode.TAA;
        }

        public void Render(ModelManager modelManager, LightManager lightManager, Camera camera, float dT)
        {
            // Update Temporal AntiAliasing stuff
            {
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.None || TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
                {
                    gpuTaaData.MipmapBias = 0.0f;
                }
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
                {
                    gpuTaaData.SampleCount = TAASamples;
                }
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2)
                {
                    gpuTaaData.MipmapBias = FSR2Wrapper.GetRecommendedMipmapBias(RenderResolution.X, PresentationResolution.X) + FSR2AdditionalMipBias;
                    gpuTaaData.SampleCount = FSR2Wrapper.GetRecommendedSampleCount(RenderResolution.X, PresentationResolution.X);
                }
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.None)
                {
                    gpuTaaData.Jitter = new Vector2(0.0f);
                }
                else
                {
                    Vector2 jitter = MyMath.GetHalton2D(frameIndex++ % gpuTaaData.SampleCount, 2, 3);
                    gpuTaaData.Jitter = (jitter * 2.0f - new Vector2(1.0f)) / RenderResolution;
                }
                gpuTaaData.TemporalAntiAliasingMode = TemporalAntiAliasing;
                taaDataBuffer.UploadElements(gpuTaaData);
            }

            if (GenerateShadowMaps)
            {
                lightManager.RenderShadowMaps(modelManager, camera);
            }

            if (IsVXGI && GridReVoxelize)
            {
                if (GridFollowCamera)
                {
                    int granularity = 8;
                    Vector3i quantizedMin = (Vector3i)((camera.Position - new Vector3(35.0f, 20.0f, 35.0f)) / granularity) * granularity;
                    Vector3i quantizedMax = (Vector3i)((camera.Position + new Vector3(35.0f, 40.0f, 35.0f)) / granularity) * granularity;

                    Voxelizer.GridMin = quantizedMin;
                    Voxelizer.GridMax = quantizedMax;
                }

                // Reset instance count, don't want to miss meshes when voxelizing
                for (int i = 0; i < modelManager.DrawCommands.Length; i++)
                {
                    ref readonly GpuMesh mesh = ref modelManager.Meshes[i];
                    modelManager.DrawCommands[i].InstanceCount = mesh.InstanceCount;
                }
                modelManager.UpdateDrawCommandBuffer(0, modelManager.DrawCommands.Length);

                Voxelizer.Render(modelManager);
            }

            if (IsConfigureGridMode)
            {
                Voxelizer.DebugRender(Result);
                return;
            }

            if (IsHiZCulling)
            {
                for (int currentWritelod = 1; currentWritelod < DepthTexture.Levels; currentWritelod++)
                {
                    BBG.Rendering.Render($"Generate Main View Depth Mipmap level {currentWritelod}", new BBG.Rendering.RenderAttachmentsVerbose()
                    {
                        DepthAttachment = new BBG.Rendering.DepthAttachment()
                        {
                            Texture = DepthTexture,
                            AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.DontCare,
                            Level = currentWritelod,
                        }
                    }, new BBG.Rendering.GraphicsPipelineState()
                    {
                        EnabledCapabilities = [BBG.Rendering.Capability.DepthTest],
                        DepthFunction = BBG.Rendering.DepthFunction.Always,
                    }, () =>
                    {
                        hiZGenerateProgram.Upload(0, currentWritelod - 1);

                        BBG.Cmd.BindTextureUnit(DepthTexture, 0);
                        BBG.Cmd.UseShaderProgram(hiZGenerateProgram);

                        BBG.Rendering.InferViewportSize();
                        BBG.Rendering.DrawNonIndexed(BBG.Rendering.Topology.Triangles, 0, 3);
                    });
                }
            }

            BBG.Computing.Compute("Main view Frustum and Occlusion Culling", () =>
            {
                modelManager.ResetInstanceCounts();

                BBG.Cmd.UseShaderProgram(cullingProgram);
                BBG.Computing.Dispatch((modelManager.MeshInstances.Length + 64 - 1) / 64, 1, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
            });

            BBG.Rendering.Render("Fill G-Buffer", new BBG.Rendering.RenderAttachments()
            {
                ColorAttachments = new BBG.Rendering.ColorAttachments()
                {
                    Textures = [AlbedoAlphaTexture, NormalTexture, MetallicRoughnessTexture, EmissiveTexture, VelocityTexture],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Clear,
                },
                DepthAttachment = new BBG.Rendering.DepthAttachment()
                {
                    Texture = DepthTexture,
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Clear,
                }
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                EnabledCapabilities = [BBG.Rendering.Capability.DepthTest, BBG.Rendering.CapIf(!IsWireframe, BBG.Rendering.Capability.CullFace)],
                FillMode = IsWireframe ? BBG.Rendering.FillMode.Line : BBG.Rendering.FillMode.Fill,
            }, () =>
            {
                BBG.Cmd.UseShaderProgram(gBufferProgram);

                BBG.Rendering.InferViewportSize();
                if (TakeMeshShaderPath)
                {
                    modelManager.MeshShaderDrawNV();
                }
                else
                {
                    modelManager.Draw();
                }
            });

            // Note: Only needed because of AMD driver bug. Should open bug ticket one day.
            // See discussion https://discord.com/channels/318590007881236480/318783155744145411/1070453712021098548
            if (BBG.GetDeviceInfo().Vendor == BBG.GpuVendor.AMD)
            {
                BBG.Cmd.Flush();
            }

            if (ShadowMode == ShadowTechnique.RayTraced)
            {
                lightManager.ComputeRayTracedShadows(RayTracingSamples);
            }
            if (IsSSAO)
            {
                SSAO.Compute();
            }
            if (IsVXGI)
            {
                ConeTracer.Compute(Voxelizer.ResultVoxels);
            }

            BBG.Rendering.Render("Deferred Lighting", new BBG.Rendering.RenderAttachments()
            {
                ColorAttachments = new BBG.Rendering.ColorAttachments()
                {
                    Textures = [resultBeforeTAA],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.DontCare,
                }
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                EnabledCapabilities = [BBG.Rendering.CapIf(IsVariableRateShading, BBG.Rendering.Capability.VariableRateShadingNV)],
                VariableRateShading = LightingVRS.GetVariableRateShading(),
            }, () =>
            {
                deferredLightingProgram.Upload("ShadowMode", (int)ShadowMode);
                deferredLightingProgram.Upload("IsVXGI", IsVXGI);

                BBG.Cmd.BindTextureUnit(SSAO.Result, 0, IsSSAO);
                BBG.Cmd.BindTextureUnit(ConeTracer.Result, 1, IsVXGI);
                BBG.Cmd.UseShaderProgram(deferredLightingProgram);

                BBG.Rendering.InferViewportSize();
                BBG.Rendering.DrawNonIndexed(BBG.Rendering.Topology.Triangles, 0, 3);
            });

            BBG.Rendering.Render("Draw lights", new BBG.Rendering.RenderAttachments()
            {
                ColorAttachments = new BBG.Rendering.ColorAttachments()
                {
                    Textures = [resultBeforeTAA, NormalTexture, EmissiveTexture, VelocityTexture],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Load,
                },
                DepthAttachment = new BBG.Rendering.DepthAttachment()
                {
                    Texture = DepthTexture,
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Load,
                }
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                EnabledCapabilities = [
                    BBG.Rendering.Capability.DepthTest, BBG.Rendering.Capability.CullFace,
                    BBG.Rendering.CapIf(IsVariableRateShading, BBG.Rendering.Capability.VariableRateShadingNV)
                ],
                VariableRateShading = LightingVRS.GetVariableRateShading(),
            }, () =>
            {
                BBG.Rendering.InferViewportSize();

                lightManager.Draw();
            });

            BBG.Rendering.Render("Draw skybox", new BBG.Rendering.RenderAttachments()
            {
                ColorAttachments = new BBG.Rendering.ColorAttachments()
                {
                    Textures = [resultBeforeTAA, VelocityTexture],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Load,
                },
                DepthAttachment = new BBG.Rendering.DepthAttachment()
                {
                    Texture = DepthTexture,
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Load,
                }
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                EnabledCapabilities = [
                    BBG.Rendering.Capability.DepthTest,
                    BBG.Rendering.CapIf(IsVariableRateShading, BBG.Rendering.Capability.VariableRateShadingNV)
                ],
                DepthFunction = BBG.Rendering.DepthFunction.Lequal,
                VariableRateShading = LightingVRS.GetVariableRateShading(),
            }, () =>
            {
                BBG.Cmd.UseShaderProgram(skyBoxProgram);
                BBG.Rendering.DrawNonIndexed(BBG.Rendering.Topology.Quads, 0, 24);
            });

            if (IsVariableRateShading || LightingVRS.Settings.DebugValue != LightingShadingRateClassifier.DebugMode.None)
            {
                LightingVRS.Compute(resultBeforeTAA);
            }

            if (IsSSR)
            {
                SSR.Compute(resultBeforeTAA);
            }

            BBG.Computing.Compute("Merge Textures", () =>
            {
                BBG.Cmd.BindImageUnit(resultBeforeTAA, 0);
                BBG.Cmd.BindTextureUnit(resultBeforeTAA, 0, resultBeforeTAA != null);
                BBG.Cmd.BindTextureUnit(SSR.Result, 1, IsSSR);
                BBG.Cmd.UseShaderProgram(mergeLightingProgram);

                BBG.Computing.Dispatch((RenderResolution.X + 8 - 1) / 8, (RenderResolution.Y + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });

            if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
            {
                TaaResolve.Compute(resultBeforeTAA);
            }
            else if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2)
            {
                FSR2Wrapper.RunFSR2(gpuTaaData.Jitter, resultBeforeTAA, DepthTexture, VelocityTexture, camera, dT * 1000.0f);

                // This is a hack to fix global UBO bindings modified by FSR2
                taaDataBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 3);
                SkyBoxManager.skyBoxTextureBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 4);
                Voxelizer.voxelizerDataBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 5);
                gBufferData.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 6);
            }
        }

        public void SetSize(Vector2i renderSize, Vector2i renderPresentationSize)
        {
            RenderResolution = renderSize;
            PresentationResolution = renderPresentationSize;

            if (TaaResolve != null) TaaResolve.SetSize(renderPresentationSize);
            if (FSR2Wrapper != null) FSR2Wrapper.SetSize(renderSize, renderPresentationSize);

            SSAO.SetSize(renderSize);
            SSR.SetSize(renderSize);
            LightingVRS.SetSize(renderSize);
            ConeTracer.SetSize(renderSize);

            if (resultBeforeTAA != null) resultBeforeTAA.Dispose();
            resultBeforeTAA = new BBG.Texture(BBG.Texture.Type.Texture2D);
            resultBeforeTAA.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            resultBeforeTAA.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            resultBeforeTAA.ImmutableAllocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

            DisposeBindlessGBufferTextures();

            AlbedoAlphaTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            AlbedoAlphaTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            AlbedoAlphaTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            AlbedoAlphaTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R8G8B8A8Unorm);
            gpuGBufferData.AlbedoAlphaTextureHandle = AlbedoAlphaTexture.GetTextureHandleARB();

            NormalTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            NormalTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            NormalTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            NormalTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R8G8Snorm);
            gpuGBufferData.NormalTextureHandle = NormalTexture.GetTextureHandleARB();

            MetallicRoughnessTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            MetallicRoughnessTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            MetallicRoughnessTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            MetallicRoughnessTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R8G8Unorm);
            gpuGBufferData.MetallicRoughnessTextureHandle = MetallicRoughnessTexture.GetTextureHandleARB();

            EmissiveTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            EmissiveTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            EmissiveTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            EmissiveTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R11G11B10Float);
            gpuGBufferData.EmissiveTextureHandle = EmissiveTexture.GetTextureHandleARB();

            VelocityTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            VelocityTexture.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
            VelocityTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            VelocityTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R16G16Float);
            gpuGBufferData.VelocityTextureHandle = VelocityTexture.GetTextureHandleARB();

            DepthTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            DepthTexture.SetFilter(BBG.Sampler.MinFilter.NearestMipmapNearest, BBG.Sampler.MagFilter.Nearest);
            DepthTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            DepthTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.D32Float, BBG.Texture.GetMaxMipmapLevel(renderSize.X, renderSize.Y, 1));
            gpuGBufferData.DepthTextureHandle = DepthTexture.GetTextureHandleARB();

            gBufferData.UploadElements(gpuGBufferData);
        }

        private void DisposeBindlessGBufferTextures()
        {
            if (AlbedoAlphaTexture != null) AlbedoAlphaTexture.Dispose();
            if (NormalTexture != null) NormalTexture.Dispose();
            if (MetallicRoughnessTexture != null) MetallicRoughnessTexture.Dispose();
            if (VelocityTexture != null) VelocityTexture.Dispose();
            if (DepthTexture != null) DepthTexture.Dispose();
        }

        public void Dispose()
        {
            if (TaaResolve != null)
            {
                TaaResolve.Dispose();
                TaaResolve = null;
            }
            if (FSR2Wrapper != null)
            {
                FSR2Wrapper.Dispose();
                FSR2Wrapper = null;
            }

            SSAO.Dispose();
            SSR.Dispose();
            LightingVRS.Dispose();
            Voxelizer.Dispose();
            ConeTracer.Dispose();

            resultBeforeTAA.Dispose();

            DisposeBindlessGBufferTextures();

            gBufferProgram.Dispose();
            deferredLightingProgram.Dispose();
            skyBoxProgram.Dispose();
            mergeLightingProgram.Dispose();
            cullingProgram.Dispose();
            hiZGenerateProgram.Dispose();

            gBufferData.Dispose();
            taaDataBuffer.Dispose();
        }
    }
}
