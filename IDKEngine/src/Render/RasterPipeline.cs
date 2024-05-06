using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;
using IDKEngine.OpenGL;
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

                if (_takeMeshShaderPathCamera && !Helper.IsExtensionsAvailable("GL_NV_mesh_shader"))
                {
                    Logger.Log(Logger.LogLevel.Error, $"Mesh shader path requires GL_NV_mesh_shader");
                    _takeMeshShaderPathCamera = false;
                }

                if (gBufferProgram != null) gBufferProgram.Dispose();
                AbstractShaderProgram.ShaderInsertions["TAKE_MESH_SHADER_PATH_CAMERA"] = TakeMeshShaderPath ? "1" : "0";

                if (TakeMeshShaderPath)
                {
                    gBufferProgram = new AbstractShaderProgram(
                        new AbstractShader((ShaderType)NvMeshShader.TaskShaderNv, "GBuffer/MeshPath/task.glsl"),
                        new AbstractShader((ShaderType)NvMeshShader.MeshShaderNv, "GBuffer/MeshPath/mesh.glsl"),
                        new AbstractShader(ShaderType.FragmentShader, "GBuffer/fragment.glsl"));
                }
                else
                {
                    gBufferProgram = new AbstractShaderProgram(
                        new AbstractShader(ShaderType.VertexShader, "GBuffer/VertexPath/vertex.glsl"),
                        new AbstractShader(ShaderType.FragmentShader, "GBuffer/fragment.glsl"));
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
                AbstractShaderProgram.ShaderInsertions["IS_HI_Z_CULLING"] = IsHiZCulling ? "1" : "0";
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
                    TaaResolve = new TAAResolve(PresentationResolution);
                }

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2 && FSR2Wrapper == null)
                {
                    FSR2Wrapper = new FSR2Wrapper(RenderResolution, PresentationResolution);
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

                return resultBeforeTAA;
            }
        }

        private Texture resultBeforeTAA;

        public Texture AlbedoAlphaTexture;
        public Texture NormalSpecularTexture;
        public Texture EmissiveRoughnessTexture;
        public Texture VelocityTexture;
        public Texture DepthTexture;

        private AbstractShaderProgram gBufferProgram;
        private readonly AbstractShaderProgram lightingProgram;
        private readonly AbstractShaderProgram skyBoxProgram;
        private readonly AbstractShaderProgram mergeLightingProgram;
        private readonly AbstractShaderProgram hiZGenerateProgram;
        private readonly AbstractShaderProgram cullingProgram;

        private readonly Framebuffer gBufferFBO;
        private readonly Framebuffer deferredLightingFBO;
        private readonly Framebuffer hiZDownsampleFBO;

        private readonly TypedBuffer<GpuTaaData> taaDataBuffer;
        private GpuTaaData gpuTaaData;

        private readonly TypedBuffer<GpuGBuffer> gBufferData;
        private GpuGBuffer gpuGBufferData;

        private int frameIndex;
        public RasterPipeline(Vector2i renderSize, Vector2i renderPresentationSize)
        {
            TakeMeshShaderPath = false;
            IsHiZCulling = false;

            lightingProgram = new AbstractShaderProgram(
                new AbstractShader(ShaderType.VertexShader, "ToScreen/vertex.glsl"),
                new AbstractShader(ShaderType.FragmentShader, "DeferredLighting/fragment.glsl"));

            skyBoxProgram = new AbstractShaderProgram(
                new AbstractShader(ShaderType.VertexShader, "SkyBox/vertex.glsl"),
                new AbstractShader(ShaderType.FragmentShader, "SkyBox/fragment.glsl"));

            hiZGenerateProgram = new AbstractShaderProgram(
                new AbstractShader(ShaderType.VertexShader, "ToScreen/vertex.glsl"),
                new AbstractShader(ShaderType.FragmentShader, "MeshCulling/Camera/HiZGenerate/fragment.glsl"));

            cullingProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "MeshCulling/Camera/Cull/compute.glsl"));

            mergeLightingProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "MergeTextures/compute.glsl"));

            taaDataBuffer = new TypedBuffer<GpuTaaData>();
            taaDataBuffer.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, 1);
            taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

            gBufferData = new TypedBuffer<GpuGBuffer>();
            gBufferData.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, 1);
            gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);

            gBufferFBO = new Framebuffer();
            deferredLightingFBO = new Framebuffer();
            hiZDownsampleFBO = new Framebuffer();

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
            TemporalAntiAliasing = TemporalAntiAliasingMode.TAA;
        }

        public void Render(ModelSystem modelSystem, LightManager lightManager, Camera camera, float dT)
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
                    gpuTaaData.MipmapBias = FSR2Wrapper.GetRecommendedMipmapBias(RenderResolution.X, PresentationResolution.X) + FSR2AddMipBias;
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
                lightManager.RenderShadowMaps(modelSystem, camera);
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
                for (int i = 0; i < modelSystem.DrawCommands.Length; i++)
                {
                    ref readonly GpuMesh mesh = ref modelSystem.Meshes[i];
                    modelSystem.DrawCommands[i].InstanceCount = mesh.InstanceCount;
                }
                modelSystem.UpdateDrawCommandBuffer(0, modelSystem.DrawCommands.Length);

                Voxelizer.Render(modelSystem);
                Voxelizer.ResultVoxels.BindToUnit(1);
            }

            if (IsConfigureGridMode)
            {
                Voxelizer.DebugRender(Result);
                return;
            }

            // G Buffer generation
            {
                HiZGenerate();

                // Frustum + Occlusion Culling
                {
                    modelSystem.ResetInstancesBeforeCulling();

                    cullingProgram.Use();
                    GL.DispatchCompute((modelSystem.MeshInstances.Length + 64 - 1) / 64, 1, 1);
                    GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
                }

                GL.DepthFunc(DepthFunction.Less);
                GL.Enable(EnableCap.CullFace);

                if (IsWireframe)
                {
                    GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                    GL.Disable(EnableCap.CullFace);
                }

                gBufferFBO.Bind();
                gBufferFBO.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                GL.Viewport(0, 0, RenderResolution.X, RenderResolution.Y);

                gBufferProgram.Use();
                if (TakeMeshShaderPath)
                {
                    modelSystem.MeshShaderDrawNV();
                }
                else
                {
                    modelSystem.Draw();
                }

                if (IsWireframe)
                {
                    GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                }
            }

            // Note: Only needed because of broken AMD drivers (should open bug ticket one day)
            // See discussion https://discord.com/channels/318590007881236480/318783155744145411/1070453712021098548
            GL.Flush();

            // Compute stuff from G Buffer that is needed later when Shading
            {
                if (ShadowMode == ShadowTechnique.RayTraced)
                {
                    lightManager.ComputeRayTracedShadows(RayTracingSamples);
                }

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
                    ConeTracer.Compute(Voxelizer.ResultVoxels);
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
                GL.Viewport(0, 0, RenderResolution.X, RenderResolution.Y);

                lightingProgram.Upload("ShadowMode", (int)ShadowMode);
                lightingProgram.Upload("IsVXGI", IsVXGI);

                deferredLightingFBO.Bind();
                lightingProgram.Use();
                GL.Disable(EnableCap.DepthTest);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                GL.Enable(EnableCap.DepthTest);
            }

            // Forward lighting
            {
                GL.Viewport(0, 0, RenderResolution.X, RenderResolution.Y);

                gBufferFBO.Bind();
                lightManager.Draw();

                GL.Disable(EnableCap.CullFace);
                GL.DepthFunc(DepthFunction.Lequal);
                skyBoxProgram.Use();
                GL.DrawArrays(PrimitiveType.Quads, 0, 24);
            }

            if (IsVariableRateShading)
            {
                VariableRateShading.Deactivate();
            }

            if (IsVariableRateShading || LightingVRS.Settings.DebugValue != LightingShadingRateClassifier.DebugMode.NoDebug)
            {
                LightingVRS.Compute(resultBeforeTAA);
            }

            if (IsSSR)
            {
                SSR.Compute(resultBeforeTAA);
            }

            MergeTextures(resultBeforeTAA, resultBeforeTAA, IsSSR ? SSR.Result : null);

            if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
            {
                TaaResolve.RunTAA(resultBeforeTAA);
            }
            else if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2)
            {
                FSR2Wrapper.RunFSR2(gpuTaaData.Jitter, resultBeforeTAA, DepthTexture, VelocityTexture, camera, dT * 1000.0f);

                // This is a hack to fix global UBO bindings modified by FSR2
                taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);
                SkyBoxManager.skyBoxTextureBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 4);
                Voxelizer.voxelizerDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);
                gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);
            }
        }

        public void HiZGenerate()
        {
            GL.DepthFunc(DepthFunction.Always);

            DepthTexture.BindToUnit(0);
            hiZDownsampleFBO.Bind();
            hiZGenerateProgram.Use();
            for (int currentWritelod = 1; currentWritelod < DepthTexture.Levels; currentWritelod++)
            {
                hiZGenerateProgram.Upload(0, currentWritelod - 1);
                hiZDownsampleFBO.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture, currentWritelod);

                Vector3i mipLevelSize = Texture.GetMipMapLevelSize(DepthTexture.Width, DepthTexture.Height, 1, currentWritelod);
                GL.Viewport(0, 0, mipLevelSize.X, mipLevelSize.Y);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }

        public void MergeTextures(Texture result, Texture a, Texture b)
        {
            result.BindToImageUnit(0, result.TextureFormat);

            if (a != null) resultBeforeTAA.BindToUnit(0);
            else Texture.UnbindFromUnit(0);

            if (b != null) SSR.Result.BindToUnit(1);
            else Texture.UnbindFromUnit(1);

            mergeLightingProgram.Use();
            GL.DispatchCompute((RenderResolution.X + 8 - 1) / 8, (RenderResolution.Y + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
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
            resultBeforeTAA = new Texture(Texture.Type.Texture2D);
            resultBeforeTAA.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            resultBeforeTAA.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            resultBeforeTAA.ImmutableAllocate(renderSize.X, renderSize.Y, 1, Texture.InternalFormat.R16G16B16A16Float);

            DisposeBindlessGBufferTextures();

            AlbedoAlphaTexture = new Texture(Texture.Type.Texture2D);
            AlbedoAlphaTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            AlbedoAlphaTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            AlbedoAlphaTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, Texture.InternalFormat.R8G8B8A8Unorm);
            gpuGBufferData.AlbedoAlphaTextureHandle = AlbedoAlphaTexture.GetTextureHandleARB();

            NormalSpecularTexture = new Texture(Texture.Type.Texture2D);
            NormalSpecularTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            NormalSpecularTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            NormalSpecularTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, Texture.InternalFormat.R8G8B8A8Snorm);
            gpuGBufferData.NormalSpecularTextureHandle = NormalSpecularTexture.GetTextureHandleARB();

            EmissiveRoughnessTexture = new Texture(Texture.Type.Texture2D);
            EmissiveRoughnessTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            EmissiveRoughnessTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            EmissiveRoughnessTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, Texture.InternalFormat.R16G16B16A16Float);
            gpuGBufferData.EmissiveRoughnessTextureHandle = EmissiveRoughnessTexture.GetTextureHandleARB();

            VelocityTexture = new Texture(Texture.Type.Texture2D);
            VelocityTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            VelocityTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            VelocityTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, Texture.InternalFormat.R16G16Float);
            gpuGBufferData.VelocityTextureHandle = VelocityTexture.GetTextureHandleARB();

            DepthTexture = new Texture(Texture.Type.Texture2D);
            DepthTexture.SetFilter(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);
            DepthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            DepthTexture.ImmutableAllocate(renderSize.X, renderSize.Y, 1, Texture.InternalFormat.D32Float, Texture.GetMaxMipmapLevel(renderSize.X, renderSize.Y, 1));
            gpuGBufferData.DepthTextureHandle = DepthTexture.GetTextureHandleARB();

            gBufferData.UploadElements(gpuGBufferData);

            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment0, resultBeforeTAA);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment1, AlbedoAlphaTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment2, NormalSpecularTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment3, EmissiveRoughnessTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment4, VelocityTexture);
            gBufferFBO.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);
            gBufferFBO.SetDrawBuffers([DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3, DrawBuffersEnum.ColorAttachment4]);

            deferredLightingFBO.SetRenderTarget(FramebufferAttachment.ColorAttachment0, resultBeforeTAA);
        }

        private void DisposeBindlessGBufferTextures()
        {
            if (AlbedoAlphaTexture != null) AlbedoAlphaTexture.Dispose();
            if (NormalSpecularTexture != null) NormalSpecularTexture.Dispose();
            if (EmissiveRoughnessTexture != null) EmissiveRoughnessTexture.Dispose();
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
            lightingProgram.Dispose();
            skyBoxProgram.Dispose();
            mergeLightingProgram.Dispose();
            cullingProgram.Dispose();
            hiZGenerateProgram.Dispose();

            gBufferFBO.Dispose();
            deferredLightingFBO.Dispose();
            hiZDownsampleFBO.Dispose();

            gBufferData.Dispose();
            taaDataBuffer.Dispose();
        }
    }
}
