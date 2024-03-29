﻿using System;
using System.IO;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class RasterPipeline : IDisposable
    {
        private static readonly bool TAKE_MESH_SHADER_PATH = false; // Helper.IsExtensionsAvailable("GL_NV_mesh_shader")

        public Vector2i RenderResolution { get; private set; }
        public Vector2i PresentationResolution { get; private set; }

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
                    TaaResolve = new TAAResolve(PresentationResolution.X, PresentationResolution.Y);
                }

                if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2 && FSR2Wrapper == null)
                {
                    FSR2Wrapper = new FSR2Wrapper(PresentationResolution.X, PresentationResolution.Y, RenderResolution.X, RenderResolution.Y);
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
        public bool IsSSAO;
        public bool IsSSR;
        public bool IsVariableRateShading;
        public bool IsConfigureGrid;
        public bool GenerateShadowMaps;
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

                return resultBeforeTAA;
            }
        }

        private Texture resultBeforeTAA;

        public Texture AlbedoAlphaTexture;
        public Texture NormalSpecularTexture;
        public Texture EmissiveRoughnessTexture;
        public Texture VelocityTexture;
        public Texture DepthTexture;

        private readonly ShaderProgram gBufferProgram;
        private readonly ShaderProgram lightingProgram;
        private readonly ShaderProgram skyBoxProgram;
        private readonly ShaderProgram mergeLightingProgram;
        private readonly ShaderProgram hiZGenerateProgram;
        private readonly ShaderProgram cullingProgram;

        private readonly Framebuffer gBufferFBO;
        private readonly Framebuffer deferredLightingFBO;
        private readonly Framebuffer hiZDownsampleFBO;

        private readonly TypedBuffer<GpuTaaData> taaDataBuffer;
        private GpuTaaData gpuTaaData;

        private readonly TypedBuffer<GpuGBuffer> gBufferData;
        private GpuGBuffer gpuGBufferData;

        private int frameIndex;
        public RasterPipeline(int width, int height, int renderPresentationWidth, int renderPresentationHeight)
        {
            LightingVRS = new LightingShadingRateClassifier(width, height, LightingShadingRateClassifier.GpuSettings.Default);

            SSAO = new SSAO(width, height, SSAO.GpuSettings.Default);
            SSR = new SSR(width, height, SSR.GpuSettings.Default);
            Voxelizer = new Voxelizer(256, 256, 256, new Vector3(-28.0f, -3.0f, -17.0f), new Vector3(28.0f, 20.0f, 17.0f));
            ConeTracer = new ConeTracer(width, height);
            
            if (TAKE_MESH_SHADER_PATH)
            {
                gBufferProgram = new ShaderProgram(
                    Shader.ShaderFromFile((ShaderType)NvMeshShader.TaskShaderNv, "GBuffer/MeshPath/task.glsl"),
                    Shader.ShaderFromFile((ShaderType)NvMeshShader.MeshShaderNv, "GBuffer/MeshPath/mesh.glsl"),
                    Shader.ShaderFromFile(ShaderType.FragmentShader, "GBuffer/fragment.glsl"));
            }
            else
            {
                gBufferProgram = new ShaderProgram(
                    Shader.ShaderFromFile(ShaderType.VertexShader, "GBuffer/VertexPath/vertex.glsl"),
                    Shader.ShaderFromFile(ShaderType.FragmentShader, "GBuffer/fragment.glsl"));
            }


            lightingProgram = new ShaderProgram(
                Shader.ShaderFromFile(ShaderType.VertexShader, "ToScreen/vertex.glsl"),
                Shader.ShaderFromFile(ShaderType.FragmentShader, "DeferredLighting/fragment.glsl"));

            skyBoxProgram = new ShaderProgram(
                Shader.ShaderFromFile(ShaderType.VertexShader, "SkyBox/vertex.glsl"),
                Shader.ShaderFromFile(ShaderType.FragmentShader, "SkyBox/fragment.glsl"));

            hiZGenerateProgram = new ShaderProgram(
                Shader.ShaderFromFile(ShaderType.VertexShader, "ToScreen/vertex.glsl"),
                Shader.ShaderFromFile(ShaderType.FragmentShader, "MeshCulling/Camera/HiZGenerate/fragment.glsl"));

            Dictionary<string, string> cullingShaderInsertions = new Dictionary<string, string>();
            cullingShaderInsertions.Add(nameof(TAKE_MESH_SHADER_PATH), TAKE_MESH_SHADER_PATH ? "1" : "0");
            cullingProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "MeshCulling/Camera/Cull/compute.glsl", cullingShaderInsertions));

            mergeLightingProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "MergeTextures/compute.glsl"));

            taaDataBuffer = new TypedBuffer<GpuTaaData>();
            taaDataBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);
            taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

            gBufferData = new TypedBuffer<GpuGBuffer>();
            gBufferData.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);
            gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);

            gBufferFBO = new Framebuffer();
            deferredLightingFBO = new Framebuffer();
            hiZDownsampleFBO = new Framebuffer();

            IsWireframe = false;
            IsSSAO = true;
            IsSSR = false;
            IsVariableRateShading = false;
            IsVXGI = false;
            GenerateShadowMaps = true;
            ShouldReVoxelize = true;
            RayTracingSamples = 1;

            SetSize(width, height, renderPresentationWidth, renderPresentationHeight);

            TemporalAntiAliasing = TemporalAntiAliasingMode.TAA;
            ShadowMode = ShadowTechnique.PcfShadowMap;
        }

        public void Render(ModelSystem modelSystem, LightManager lightManager, Camera camera, float dT)
        {
            if (GenerateShadowMaps)
            {
                lightManager.RenderShadowMaps(modelSystem, camera);
            }

            // Update Temporal AntiAliasing related stuff
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
                    Vector2 jitter = MyMath.GetHalton2D(frameIndex % gpuTaaData.SampleCount, 2, 3);
                    gpuTaaData.Jitter = (jitter * 2.0f - new Vector2(1.0f)) / RenderResolution;
                }
                gpuTaaData.TemporalAntiAliasingMode = TemporalAntiAliasing;
                taaDataBuffer.UploadElements(gpuTaaData);

                frameIndex++;
            }

            if (IsVXGI && ShouldReVoxelize)
            {
                // Reset instance count, don't want to miss meshes when voxelizing
                for (int i = 0; i < modelSystem.DrawCommands.Length; i++)
                {
                    ref readonly GpuMesh mesh = ref modelSystem.Meshes[i];
                    modelSystem.DrawCommands[i].InstanceCount = mesh.InstanceCount;
                }
                modelSystem.UpdateDrawCommandBuffer(0, modelSystem.DrawCommands.Length);

                Voxelizer.Render(modelSystem);
                Voxelizer.ResultVoxelsAlbedo.BindToUnit(1);
            }

            if (IsConfigureGrid)
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
                if (TAKE_MESH_SHADER_PATH)
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

            // only needed because of broken amd drivers
            GL.Flush();

            // Compute stuff from G Buffer that is needed later when Shading, like SSAO
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
                GL.Viewport(0, 0, RenderResolution.X, RenderResolution.Y);

                deferredLightingFBO.Bind();
                lightingProgram.Use();
                GL.Disable(EnableCap.DepthTest);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                GL.Enable(EnableCap.DepthTest);
            }

            // Forward rendering
            {
                GL.Viewport(0, 0, RenderResolution.X, RenderResolution.Y);

                gBufferFBO.Bind();
                if (lightManager != null)
                {
                    lightManager.Draw();
                }

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

            {
                resultBeforeTAA.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, resultBeforeTAA.SizedInternalFormat);
                resultBeforeTAA.BindToUnit(0);

                if (IsSSR) SSR.Result.BindToUnit(1);
                else Texture.UnbindFromUnit(1);

                mergeLightingProgram.Use();
                GL.DispatchCompute((RenderResolution.X + 8 - 1) / 8, (RenderResolution.Y + 8 - 1) / 8, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
            }

            if (TemporalAntiAliasing == TemporalAntiAliasingMode.TAA)
            {
                TaaResolve.RunTAA(resultBeforeTAA);
            }
            else if (TemporalAntiAliasing == TemporalAntiAliasingMode.FSR2)
            {
                FSR2Wrapper.RunFSR2(gpuTaaData.Jitter, resultBeforeTAA, DepthTexture, VelocityTexture, camera, dT * 1000.0f);

                // TODO: This is a hack to fix global UBO bindings modified by FSR2
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

        private void DisposeBindlessTextures()
        {
            if (AlbedoAlphaTexture != null) { AlbedoAlphaTexture.Dispose(); }
            if (NormalSpecularTexture != null) {  NormalSpecularTexture.Dispose(); }
            if (EmissiveRoughnessTexture != null) { EmissiveRoughnessTexture.Dispose(); }
            if (VelocityTexture != null) { VelocityTexture.Dispose(); }
            if (DepthTexture != null) { DepthTexture.Dispose(); }
        }

        public void SetSize(int renderWidth, int renderHeight, int presentationWidth, int presentationHeight)
        {
            RenderResolution = new Vector2i(renderWidth, renderHeight);
            PresentationResolution = new Vector2i(presentationWidth, presentationHeight);

            if (TaaResolve != null) TaaResolve.SetSize(presentationWidth, presentationHeight);
            if (FSR2Wrapper != null) FSR2Wrapper.SetSize(renderWidth, renderHeight, presentationWidth, presentationHeight);

            SSAO.SetSize(renderWidth, renderHeight);
            SSR.SetSize(renderWidth, renderHeight);
            LightingVRS.SetSize(renderWidth, renderHeight);
            ConeTracer.SetSize(renderWidth, renderHeight);

            if (resultBeforeTAA != null) resultBeforeTAA.Dispose();
            resultBeforeTAA = new Texture(TextureTarget2d.Texture2D);
            resultBeforeTAA.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            resultBeforeTAA.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            resultBeforeTAA.ImmutableAllocate(renderWidth, renderHeight, 1, SizedInternalFormat.Rgba16f);

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
            DepthTexture.SetFilter(TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest);
            DepthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            DepthTexture.ImmutableAllocate(renderWidth, renderHeight, 1, SizedInternalFormat.DepthComponent24, Texture.GetMaxMipmapLevel(renderWidth, renderHeight, 1));
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

            DisposeBindlessTextures();

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
