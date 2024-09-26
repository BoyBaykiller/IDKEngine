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
        public enum ShadowTechnique : int
        {
            None,
            PcfShadowMap,
            RayTraced
        }

        public enum TemporalAntiAliasingMode : int
        {
            None,
            TAA,
            FSR2,
        }

        private TemporalAntiAliasingMode _temporalAntiAliasingMode;
        public TemporalAntiAliasingMode TAAMode
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

                if (TAAMode == TemporalAntiAliasingMode.TAA)
                {
                    TaaResolve?.Dispose();
                    TaaResolve = new TAAResolve(PresentationResolution);
                }
                else
                {
                    TaaResolve?.Dispose();
                    TaaResolve = null;
                }

                if (TAAMode == TemporalAntiAliasingMode.FSR2)
                {
                    FSR2Wrapper?.Dispose();
                    FSR2Wrapper = new FSR2Wrapper(RenderResolution, PresentationResolution);
                }
                else
                {
                    FSR2Wrapper?.Dispose();
                    FSR2Wrapper = null;
                }
            }
        }

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
                       BBG.AbstractShader.FromFile(BBG.ShaderStage.TaskNV, "GBuffer/MeshPath/task.glsl"),
                       BBG.AbstractShader.FromFile(BBG.ShaderStage.MeshNV, "GBuffer/MeshPath/mesh.glsl"),
                       BBG.AbstractShader.FromFile(BBG.ShaderStage.Fragment, "GBuffer/fragment.glsl"));
                }
                else
                {
                    gBufferProgram = new BBG.AbstractShaderProgram(
                        BBG.AbstractShader.FromFile(BBG.ShaderStage.Vertex, "GBuffer/VertexPath/vertex.glsl"),
                        BBG.AbstractShader.FromFile(BBG.ShaderStage.Fragment, "GBuffer/fragment.glsl"));
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

        public BBG.Texture Result
        {
            get
            {
                if (TAAMode == TemporalAntiAliasingMode.TAA)
                {
                    return TaaResolve.Result;
                }

                if (TAAMode == TemporalAntiAliasingMode.FSR2)
                {
                    return FSR2Wrapper.Result;
                }

                return beforeTAATexture;
            }
        }

        public Vector2i RenderResolution { get; private set; }
        public Vector2i PresentationResolution { get; private set; }

        // Run at render resolution
        public readonly SSAO SSAO;
        public readonly SSR SSR;
        public readonly ConeTracer ConeTracer;
        public readonly Voxelizer Voxelizer;
        public readonly LightingShadingRateClassifier LightingVRS;

        // Run at presentation resolution
        // Only one is ever used while the other is disposed and set to null
        public TAAResolve? TaaResolve;
        public FSR2Wrapper? FSR2Wrapper;

        // Which FX is turned on
        public bool IsWireframe;
        public bool IsSSAO;
        public bool IsSSR;
        public bool IsVXGI;
        public bool IsVariableRateShading;

        // Voxelization Settings
        public bool IsConfigureGridMode;
        public bool GridReVoxelize;
        public bool GridFollowCamera;

        // TAA Settings
        public bool TAAEnableMipBias;
        public float TAAAdditionalMipBias;
        public int TAASamples;

        // Shadow Settings
        public ShadowTechnique ShadowMode;
        public bool GenerateShadowMaps;
        public int RayTracingSamples;

        // G-Buffer Attachments
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

        private BBG.Texture beforeTAATexture;

        private readonly BBG.TypedBuffer<GpuTaaData> taaDataBuffer;
        private GpuTaaData gpuTaaData;

        private readonly BBG.TypedBuffer<GpuBindlessGBuffer> bindlessGBufferBuffer;
        private GpuBindlessGBuffer gpuBindlessGBuffer;

        private int frameIndex;
        public unsafe RasterPipeline(Vector2i renderSize, Vector2i presentationSize)
        {
            TakeMeshShaderPath = false;
            IsHiZCulling = false;

            deferredLightingProgram = new BBG.AbstractShaderProgram(
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Vertex, "ToScreen/vertex.glsl"),
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Fragment, "DeferredLighting/fragment.glsl"));

            skyBoxProgram = new BBG.AbstractShaderProgram(
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Vertex, "SkyBox/vertex.glsl"),
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Fragment, "SkyBox/fragment.glsl"));

            hiZGenerateProgram = new BBG.AbstractShaderProgram(
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Vertex, "ToScreen/vertex.glsl"),
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Fragment, "MeshCulling/Camera/HiZGenerate/fragment.glsl"));

            cullingProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "MeshCulling/Camera/Cull/compute.glsl"));

            mergeLightingProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "MergeTextures/compute.glsl"));

            taaDataBuffer = new BBG.TypedBuffer<GpuTaaData>();
            taaDataBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);
            taaDataBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 3);

            bindlessGBufferBuffer = new BBG.TypedBuffer<GpuBindlessGBuffer>();
            bindlessGBufferBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);
            bindlessGBufferBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 6);

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

            SetSize(renderSize, presentationSize);

            TAAEnableMipBias = true;
            TAASamples = 6;
            TAAAdditionalMipBias = 0.25f;
            TAAMode = TemporalAntiAliasingMode.TAA;
        }

        public void Render(ModelManager modelManager, LightManager lightManager, Camera camera, float dT)
        {
            // Update Temporal AntiAliasing stuff
            {
                gpuTaaData.MipmapBias = 0.0f;
                gpuTaaData.Jitter = new Vector2(0.0f);
                gpuTaaData.TemporalAntiAliasingMode = TAAMode;
                if (TAAMode == TemporalAntiAliasingMode.TAA)
                {
                    gpuTaaData.MipmapBias = TAAResolve.GetRecommendedMipmapBias(RenderResolution.X, PresentationResolution.X) + TAAAdditionalMipBias;
                    gpuTaaData.SampleCount = TAASamples;
                }
                if (TAAMode == TemporalAntiAliasingMode.FSR2)
                {
                    gpuTaaData.MipmapBias = FSR2Wrapper.GetRecommendedMipmapBias(RenderResolution.X, PresentationResolution.X) + TAAAdditionalMipBias;
                    gpuTaaData.SampleCount = FSR2Wrapper.GetRecommendedSampleCount(RenderResolution.X, PresentationResolution.X);
                }
                if (TAAMode == TemporalAntiAliasingMode.TAA || 
                    TAAMode == TemporalAntiAliasingMode.FSR2)
                {
                    Vector2 jitter = MyMath.GetHalton2D(frameIndex++ % gpuTaaData.SampleCount, 2, 3);
                    gpuTaaData.Jitter = (jitter * 2.0f - new Vector2(1.0f)) / RenderResolution;
                }
                if (!TAAEnableMipBias)
                {
                    gpuTaaData.MipmapBias = 0.0f;
                }

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
                modelManager.UploadDrawCommandBuffer(0, modelManager.DrawCommands.Length);

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
                        DepthStencilAttachment = new BBG.Rendering.DepthStencilAttachment()
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
                DepthStencilAttachment = new BBG.Rendering.DepthStencilAttachment()
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

            // The AMD driver fails to detect a write-read dependency between G-Buffer and some of the
            // following passes like RayTraced shadows. Likely because the G-Buffer is bindless textures.
            // TextureBarrier fixes it only on newer drivers (somewhere arround 24.9.2 RC1)
            // Flush works on both older and newer driver.
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
                    Textures = [beforeTAATexture],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.DontCare,
                }
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                EnabledCapabilities = [BBG.Rendering.CapIf(IsVariableRateShading, BBG.Rendering.Capability.VariableRateShadingNV)],
                VariableRateShading = LightingVRS.GetRenderData(),
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
                    Textures = [beforeTAATexture, NormalTexture, EmissiveTexture, VelocityTexture],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Load,
                },
                DepthStencilAttachment = new BBG.Rendering.DepthStencilAttachment()
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
                VariableRateShading = LightingVRS.GetRenderData(),
            }, () =>
            {
                BBG.Rendering.InferViewportSize();

                lightManager.Draw();
            });

            BBG.Rendering.Render("Draw skybox", new BBG.Rendering.RenderAttachments()
            {
                ColorAttachments = new BBG.Rendering.ColorAttachments()
                {
                    Textures = [beforeTAATexture, VelocityTexture],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Load,
                },
                DepthStencilAttachment = new BBG.Rendering.DepthStencilAttachment()
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
                VariableRateShading = LightingVRS.GetRenderData(),
            }, () =>
            {
                BBG.Cmd.UseShaderProgram(skyBoxProgram);
                BBG.Rendering.DrawNonIndexed(BBG.Rendering.Topology.Quads, 0, 24);
            });

            if (IsVariableRateShading || LightingVRS.Settings.DebugValue != LightingShadingRateClassifier.DebugMode.None)
            {
                LightingVRS.Compute(beforeTAATexture);
            }

            if (IsSSR)
            {
                SSR.Compute(beforeTAATexture);
            }

            BBG.Computing.Compute("Merge Textures", () =>
            {
                BBG.Cmd.BindImageUnit(beforeTAATexture, 0);
                BBG.Cmd.BindTextureUnit(beforeTAATexture, 0, beforeTAATexture != null);
                BBG.Cmd.BindTextureUnit(SSR.Result, 1, IsSSR);
                BBG.Cmd.UseShaderProgram(mergeLightingProgram);

                BBG.Computing.Dispatch((RenderResolution.X + 8 - 1) / 8, (RenderResolution.Y + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);
            });

            if (TAAMode == TemporalAntiAliasingMode.TAA)
            {
                TaaResolve.Compute(beforeTAATexture);
            }
            else if (TAAMode == TemporalAntiAliasingMode.FSR2)
            {
                FSR2Wrapper.Run(beforeTAATexture, DepthTexture, VelocityTexture, camera, gpuTaaData.Jitter, dT * 1000.0f);

                // This is a hack to fix global UBO bindings modified by FSR2
                taaDataBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 3);
                SkyBoxManager.skyBoxTextureBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 4);
                Voxelizer.voxelizerDataBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 5);
                bindlessGBufferBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 6);
            }
        }

        public void SetSize(Vector2i renderSize, Vector2i presentationSize)
        {
            RenderResolution = renderSize;
            PresentationResolution = presentationSize;

            if (TaaResolve != null) TaaResolve.SetSize(presentationSize);
            if (FSR2Wrapper != null) FSR2Wrapper.SetSize(renderSize, presentationSize);

            SSAO.SetSize(renderSize);
            SSR.SetSize(renderSize);
            LightingVRS.SetSize(renderSize);
            ConeTracer.SetSize(renderSize);

            if (beforeTAATexture != null) beforeTAATexture.Dispose();
            beforeTAATexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            beforeTAATexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            beforeTAATexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            beforeTAATexture.Allocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

            DisposeBindlessGBufferTextures();

            AlbedoAlphaTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            AlbedoAlphaTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            AlbedoAlphaTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            AlbedoAlphaTexture.Allocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R8G8B8A8Unorm);
            gpuBindlessGBuffer.AlbedoAlphaTexture = AlbedoAlphaTexture.GetTextureHandleARB();

            NormalTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            NormalTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            NormalTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            NormalTexture.Allocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R8G8Unorm);
            gpuBindlessGBuffer.NormalTexture = NormalTexture.GetTextureHandleARB();

            MetallicRoughnessTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            MetallicRoughnessTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            MetallicRoughnessTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            MetallicRoughnessTexture.Allocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R8G8Unorm);
            gpuBindlessGBuffer.MetallicRoughnessTexture = MetallicRoughnessTexture.GetTextureHandleARB();

            EmissiveTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            EmissiveTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            EmissiveTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            EmissiveTexture.Allocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R11G11B10Float);
            gpuBindlessGBuffer.EmissiveTexture = EmissiveTexture.GetTextureHandleARB();

            VelocityTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            VelocityTexture.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);
            VelocityTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            VelocityTexture.Allocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.R16G16Float);
            gpuBindlessGBuffer.VelocityTexture = VelocityTexture.GetTextureHandleARB();

            DepthTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
            DepthTexture.SetFilter(BBG.Sampler.MinFilter.NearestMipmapNearest, BBG.Sampler.MagFilter.Nearest);
            DepthTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            DepthTexture.Allocate(renderSize.X, renderSize.Y, 1, BBG.Texture.InternalFormat.D32Float, BBG.Texture.GetMaxMipmapLevel(renderSize.X, renderSize.Y, 1));
            gpuBindlessGBuffer.DepthTexture = DepthTexture.GetTextureHandleARB();

            bindlessGBufferBuffer.UploadElements(gpuBindlessGBuffer);
        }

        private void DisposeBindlessGBufferTextures()
        {
            if (AlbedoAlphaTexture != null) AlbedoAlphaTexture.Dispose();
            if (NormalTexture != null) NormalTexture.Dispose();
            if (MetallicRoughnessTexture != null) MetallicRoughnessTexture.Dispose();
            if (EmissiveTexture != null) EmissiveTexture.Dispose();
            if (VelocityTexture != null) VelocityTexture.Dispose();
            if (DepthTexture != null) DepthTexture.Dispose();
        }

        public void Dispose()
        {
            TaaResolve?.Dispose();
            TaaResolve = null;

            FSR2Wrapper?.Dispose();
            FSR2Wrapper = null;

            SSAO.Dispose();
            SSR.Dispose();
            LightingVRS.Dispose();
            Voxelizer.Dispose();
            ConeTracer.Dispose();

            beforeTAATexture.Dispose();

            DisposeBindlessGBufferTextures();

            gBufferProgram.Dispose();
            deferredLightingProgram.Dispose();
            skyBoxProgram.Dispose();
            mergeLightingProgram.Dispose();
            cullingProgram.Dispose();
            hiZGenerateProgram.Dispose();

            bindlessGBufferBuffer.Dispose();
            taaDataBuffer.Dispose();
        }
    }
}
