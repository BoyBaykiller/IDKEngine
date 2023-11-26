using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class RasterPipeline : IDisposable
    {
        public Vector2i RenderResolution { get; private set; }
        public Vector2i RenderPresentationResolution { get; private set; }

        private bool _isVXGI;
        public bool IsVXGI
        {
            get => _isVXGI;

            set
            {
                _isVXGI = value;
                lightingProgram.Upload("IsVXGI", _isVXGI);
            }
        }
        
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
                if (!FSR2Wrapper.IS_FSR2_SUPPORTED && value == TemporalAntiAliasingMode.FSR2)
                {
                    Logger.Log(Logger.LogLevel.Error, $"{TemporalAntiAliasingMode.FSR2} is Windows only");
                    return;
                }

                _temporalAntiAliasingMode = value;

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.None)
                {
                    return;
                }

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA && TaaResolve == null)
                {
                    TaaResolve = new TAAResolve(RenderPresentationResolution.X, RenderPresentationResolution.Y);
                }

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2 && FSR2Wrapper == null)
                {
                    FSR2Wrapper = new FSR2Wrapper(RenderPresentationResolution.X, RenderPresentationResolution.Y, RenderResolution.X, RenderResolution.Y);
                }
            }
        }

        public enum ShadowTechnique
        {
            None,
            PcfShadowMap,
            RayTraced
        }

        private ShadowTechnique _shadowMode;
        public ShadowTechnique ShadowMode
        {
            get => _shadowMode;

            set
            {
                _shadowMode = value;
                lightingProgram.Upload("ShadowMode", (int)ShadowMode);
            }
        }

        private int _rayTracingSampes;
        public int RayTracingSamples
        {
            get => _rayTracingSampes;

            set
            {
                _rayTracingSampes = value;
                lightingProgram.Upload("RayTracingSamples", RayTracingSamples);
            }
        }

        public bool IsWireframe;
        public bool GenerateShadowMaps;
        public bool IsSSAO;
        public bool IsSSR;
        public bool IsVariableRateShading;
        public bool IsConfigureGrid;
        public bool ShouldReVoxelize;

        public int TAASamples = 6;
        public float FSR2AddMipBias = 0.25f;

        // Runs at render presentation resolution
        public TAAResolve TaaResolve;
        public FSR2Wrapper FSR2Wrapper;
        
        // Runs at render resolution
        public readonly SSAO SSAO;
        public readonly SSR SSR;
        public readonly LightingShadingRateClassifier LightingVRS;
        public readonly ConeTracer ConeTracer;

        public readonly Voxelizer Voxelizer;

        public Texture Result
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

                return upscalerInputTexture;
            }
        }

        private Texture upscalerInputTexture;

        public Texture AlbedoAlphaTexture;
        public Texture NormalSpecularTexture;
        public Texture EmissiveRoughnessTexture;
        public Texture VelocityTexture;
        public Texture DepthTexture;

        private readonly ShaderProgram gBufferProgram;
        private readonly ShaderProgram lightingProgram;
        private readonly ShaderProgram skyBoxProgram;
        private readonly ShaderProgram mergeLightingProgram;

        private readonly Framebuffer gBufferFBO;
        private readonly Framebuffer deferredLightingFBO;

        private readonly BufferObject taaDataBuffer;
        private GpuTaaData gpuTaaData;

        private readonly BufferObject gBufferData;
        private GpuGBuffer gpuGBufferData;

        private int frameIndex;
        public unsafe RasterPipeline(int width, int height, int renderPresentationWidth, int renderPresentationHeight)
        {
            LightingVRS = new LightingShadingRateClassifier(width, height, 0.025f, 0.2f);

            SSAO = new SSAO(width, height, 10, 0.1f, 2.0f);
            SSR = new SSR(width, height, 30, 8, 50.0f);
            Voxelizer = new Voxelizer(256, 256, 256, new Vector3(-28.0f, -3.0f, -17.0f), new Vector3(28.0f, 20.0f, 17.0f));
            ConeTracer = new ConeTracer(width, height);

            gBufferProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/DeferredRendering/GBuffer/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/DeferredRendering/GBuffer/fragment.glsl")));

            lightingProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/DeferredRendering/Lighting/fragment.glsl")));

            skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/SkyBox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/SkyBox/fragment.glsl")));

            mergeLightingProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/MergeTextures/compute.glsl")));

            taaDataBuffer = new BufferObject();
            taaDataBuffer.ImmutableAllocate(sizeof(GpuTaaData), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

            gBufferData = new BufferObject();
            gBufferData.ImmutableAllocate(sizeof(GpuGBuffer), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);

            gBufferFBO = new Framebuffer();
            deferredLightingFBO = new Framebuffer();

            IsWireframe = false;
            GenerateShadowMaps = true;
            IsSSAO = true;
            IsSSR = false;
            IsVariableRateShading = false;
            IsVXGI = false;
            ShouldReVoxelize = true;
            RayTracingSamples = 1;

            SetSize(width, height, renderPresentationWidth, renderPresentationHeight);

            TemporalAntiAliasing = TemporalAntiAliasingMode.TAA;
            ShadowMode = ShadowTechnique.PcfShadowMap;
        }

        public unsafe void Render(ModelSystem modelSystem, float dT, float nearPlane, float farPlane, float cameraFovY, in Matrix4 cullProjViewMatrix, LightManager lightManager = null)
        {
            if (GenerateShadowMaps && lightManager != null)
            {
                lightManager.RenderShadowMaps(modelSystem);
            }

            // Update Temporal AntiAliasing related stuff
            {
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.None || TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
                {
                    gpuTaaData.MipmapBias = 0.0f;
                }
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
                {
                    gpuTaaData.Samples = TAASamples;
                }
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2)
                {
                    gpuTaaData.MipmapBias = FSR2Wrapper.GetRecommendedMipmapBias(RenderResolution.X, RenderPresentationResolution.X) + FSR2AddMipBias;
                    gpuTaaData.Samples = FSR2Wrapper.GetRecommendedSampleCount(RenderResolution.X, RenderPresentationResolution.X);
                }
                if (TemporalAntiAliasing == TemporalAntiAliasingMode.None)
                {
                    gpuTaaData.Jitter = new Vector2(0.0f);
                }
                else
                {
                    Vector2 jitter = MyMath.GetHalton2D(frameIndex % gpuTaaData.Samples, 2, 3);
                    gpuTaaData.Jitter = (jitter * 2.0f - new Vector2(1.0f)) / RenderResolution;
                }
                gpuTaaData.TemporalAntiAliasingMode = TemporalAntiAliasing;
                taaDataBuffer.SubData(0, sizeof(GpuTaaData), gpuTaaData);

                frameIndex++;
            }

            if (IsVXGI && ShouldReVoxelize)
            {
                // When voxelizing make sure no mesh(-instance) culled, by reuploading cpu command buffer
                modelSystem.UpdateDrawCommandBuffer(0, modelSystem.DrawCommands.Length);

                Voxelizer.Render(modelSystem);
                Voxelizer.ResultVoxelsAlbedo.BindToUnit(1);
            }

            if (IsConfigureGrid)
            {
                Voxelizer.DebugRender(Result);
            }
            else
            {
                GL.Viewport(0, 0, RenderResolution.X, RenderResolution.Y);

                // G Buffer generation
                {
                    if (IsWireframe)
                    {
                        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                    }

                    modelSystem.FrustumCull(cullProjViewMatrix);

                    gBufferFBO.Bind();
                    gBufferFBO.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    gBufferProgram.Use();
                    modelSystem.Draw();

                    if (IsWireframe)
                    {
                        GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                    }
                }

                // only needed because of broken amd drivers
                GL.Flush();

                // Compute stuff from G Buffer needed for Deferred Lighting like SSAO
                {
                    if (IsSSAO)
                    {
                        SSAO.Compute();
                        SSAO.Result.BindToUnit(0);
                    }
                    else
                    {
                        Texture.UnbindFromUnit(0);
                    }

                    if (IsVXGI)
                    {
                        ConeTracer.Compute(Voxelizer.ResultVoxelsAlbedo);
                        ConeTracer.Result.BindToUnit(1);
                    }
                    else
                    {
                        Texture.UnbindFromUnit(1);
                    }
                }

                if (IsVariableRateShading)
                {
                    VariableRateShading.Activate(LightingVRS);
                }

                // Deferred Lighting
                {
                    deferredLightingFBO.Bind();
                    lightingProgram.Use();
                    GL.DepthMask(false);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                    GL.DepthMask(true);
                }

                // Forward rendering
                {
                    gBufferFBO.Bind();
                    if (lightManager != null)
                    {
                        lightManager.Draw();
                    }

                    GL.Disable(EnableCap.CullFace);
                    GL.DepthFunc(DepthFunction.Lequal);
                    skyBoxProgram.Use();
                    GL.DrawArrays(PrimitiveType.Quads, 0, 24);
                    GL.DepthFunc(DepthFunction.Less);
                    GL.Enable(EnableCap.CullFace);
                }

                if (IsVariableRateShading)
                {
                    VariableRateShading.Deactivate();
                }

                if (IsVariableRateShading || LightingVRS.DebugValue != LightingShadingRateClassifier.DebugMode.NoDebug)
                {
                    LightingVRS.Compute(upscalerInputTexture);
                }

                if (IsSSR)
                {
                    SSR.Compute(upscalerInputTexture);
                }

                {
                    upscalerInputTexture.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, upscalerInputTexture.SizedInternalFormat);
                    upscalerInputTexture.BindToUnit(0);

                    if (IsSSR) SSR.Result.BindToUnit(1);
                    else Texture.UnbindFromUnit(1);

                    mergeLightingProgram.Use();
                    GL.DispatchCompute((RenderResolution.X + 8 - 1) / 8, (RenderResolution.Y + 8 - 1) / 8, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
                }

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
                {
                    TaaResolve.RunTAA(upscalerInputTexture);
                }
                else if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2)
                {
                    FSR2Wrapper.RunFSR2(gpuTaaData.Jitter, upscalerInputTexture, DepthTexture, VelocityTexture, dT * 1000.0f, nearPlane, farPlane, cameraFovY);

                    // TODO: This is a hack to fix global UBO bindings modified by FSR2
                    taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);
                    SkyBoxManager.skyBoxTextureBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 4);
                    Voxelizer.voxelizerDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);
                    gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);
                }
            }
        }

        private void DisposeBindlessTextures()
        {
            if (AlbedoAlphaTexture != null) { AlbedoAlphaTexture.Dispose(); }
            if (NormalSpecularTexture != null) {  NormalSpecularTexture.Dispose(); }
            if (EmissiveRoughnessTexture != null) { EmissiveRoughnessTexture.Dispose(); }
            if (VelocityTexture != null) { VelocityTexture.Dispose(); }
            if (DepthTexture != null) { DepthTexture.Dispose(); }
        }

        public unsafe void SetSize(int renderWidth, int renderHeight, int renderPresentationWidth, int renderPresentationHeight)
        {
            RenderResolution = new Vector2i(renderWidth, renderHeight);
            RenderPresentationResolution = new Vector2i(renderPresentationWidth, renderPresentationHeight);

            if (TaaResolve != null) TaaResolve.SetSize(renderPresentationWidth, renderPresentationHeight);
            if (FSR2Wrapper != null) FSR2Wrapper.SetSize(renderPresentationWidth, renderPresentationHeight, renderWidth, renderHeight);

            SSAO.SetSize(renderWidth, renderHeight);
            SSR.SetSize(renderWidth, renderHeight);
            LightingVRS.SetSize(renderWidth, renderHeight);
            ConeTracer.SetSize(renderWidth, renderHeight);

            if (upscalerInputTexture != null) upscalerInputTexture.Dispose();
            upscalerInputTexture = new Texture(TextureTarget2d.Texture2D);
            upscalerInputTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            upscalerInputTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            upscalerInputTexture.ImmutableAllocate(renderWidth, renderHeight, 1, SizedInternalFormat.Rgba16f);

            DisposeBindlessTextures();

            AlbedoAlphaTexture = new Texture(TextureTarget2d.Texture2D);
            AlbedoAlphaTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            AlbedoAlphaTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            AlbedoAlphaTexture.ImmutableAllocate(renderWidth, renderHeight, 1, SizedInternalFormat.Rgba8);
            gpuGBufferData.AlbedoAlphaTextureHandle = AlbedoAlphaTexture.GetTextureHandleARB();

            NormalSpecularTexture = new Texture(TextureTarget2d.Texture2D);
            NormalSpecularTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            NormalSpecularTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            NormalSpecularTexture.ImmutableAllocate(renderWidth, renderHeight, 1, SizedInternalFormat.Rgba8Snorm);
            gpuGBufferData.NormalSpecularTextureHandle = NormalSpecularTexture.GetTextureHandleARB();

            EmissiveRoughnessTexture = new Texture(TextureTarget2d.Texture2D);
            EmissiveRoughnessTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            EmissiveRoughnessTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            EmissiveRoughnessTexture.ImmutableAllocate(renderWidth, renderHeight, 1, SizedInternalFormat.Rgba16f);
            gpuGBufferData.EmissiveRoughnessTextureHandle = EmissiveRoughnessTexture.GetTextureHandleARB();

            VelocityTexture = new Texture(TextureTarget2d.Texture2D);
            VelocityTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            VelocityTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            VelocityTexture.ImmutableAllocate(renderWidth, renderHeight, 1, SizedInternalFormat.Rg16f);
            gpuGBufferData.VelocityTextureHandle = VelocityTexture.GetTextureHandleARB();

            DepthTexture = new Texture(TextureTarget2d.Texture2D);
            DepthTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            DepthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            DepthTexture.ImmutableAllocate(renderWidth, renderHeight, 1, (SizedInternalFormat)PixelInternalFormat.DepthComponent24);
            gpuGBufferData.DepthTextureHandle = DepthTexture.GetTextureHandleARB();

            gBufferData.SubData(0, sizeof(GpuGBuffer), gpuGBufferData);

            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment0, upscalerInputTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment1, AlbedoAlphaTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment2, NormalSpecularTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment3, EmissiveRoughnessTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment4, VelocityTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);
            gBufferFBO.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3, DrawBuffersEnum.ColorAttachment4 });

            deferredLightingFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment0, upscalerInputTexture);
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

            upscalerInputTexture.Dispose();

            DisposeBindlessTextures();

            gBufferProgram.Dispose();
            lightingProgram.Dispose();
            skyBoxProgram.Dispose();
            mergeLightingProgram.Dispose();

            gBufferFBO.Dispose();
            deferredLightingFBO.Dispose();

            gBufferData.Dispose();
            taaDataBuffer.Dispose();
        }
    }
}
