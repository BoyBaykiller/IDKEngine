using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class RasterPipeline : IDisposable
    {
        // provisionally make seperate class that does cone tracing using g buffer data

        private float _normalRayOffset;
        public float NormalRayOffset
        { 
            get => _normalRayOffset;

            set
            {
                _normalRayOffset = value;
                lightingProgram.Upload("NormalRayOffset", _normalRayOffset);
            }
        }

        private int _maxSamples;
        public int MaxSamples
        {
            get => _maxSamples;

            set
            {
                _maxSamples = value;
                lightingProgram.Upload("MaxSamples", _maxSamples);
            }
        }

        private float _giBoost;
        public float GIBoost
        {
            get => _giBoost;

            set
            {
                _giBoost = value;
                lightingProgram.Upload("GIBoost", _giBoost);
            }
        }

        private float _giSkyBoxBoost;
        public float GISkyBoxBoost
        {
            get => _giSkyBoxBoost;

            set
            {
                _giSkyBoxBoost = value;
                lightingProgram.Upload("GISkyBoxBoost", _giSkyBoxBoost);
            }
        }

        private float _stepMultiplier;
        public float StepMultiplier
        {
            get => _stepMultiplier;

            set
            {
                _stepMultiplier = value;
                lightingProgram.Upload("StepMultiplier", _stepMultiplier);
            }
        }

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

        public bool IsWireframe;
        public bool IsSSAO;
        public bool IsSSR;
        public bool IsVolumetricLighting;
        public bool IsVariableRateShading;

        public readonly SSAO SSAO;
        public readonly SSR SSR;
        public readonly VolumetricLighter VolumetricLight;
        public readonly ShadingRateClassifier ShadingRateClassifier;
        public readonly Voxelizer Voxelizer;

        public Texture Result;

        private Texture albedoAlphaTexture;
        private Texture normalSpecularTexture;
        private Texture emissiveRoughnessTexture;
        private Texture velocityTexture;
        private Texture depthTexture;

        private readonly ShaderProgram gBufferProgram;
        private readonly ShaderProgram lightingProgram;
        private readonly ShaderProgram skyBoxProgram;

        private readonly BufferObject gBufferData;
        private GLSLGBufferData glslGBufferData;
        private readonly Framebuffer framebuffer;
        public unsafe RasterPipeline(int width, int height)
        {
            NvShadingRateImage[] shadingRates = new NvShadingRateImage[]
            {
                NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
            };
            ShadingRateClassifier = new ShadingRateClassifier(shadingRates, 
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl")),
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/debugCompute.glsl")), width, height);

            SSAO = new SSAO(width, height, 10, 0.1f, 2.0f);
            SSR = new SSR(width, height, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(width, height, 7, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));
            Voxelizer = new Voxelizer(256, 256, 256, new Vector3(-28.0f, -3.0f, -17.0f), new Vector3(28.0f, 20.0f, 17.0f));

            gBufferProgram = new ShaderProgram(
                    new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/DeferredRendering/GBuffer/vertex.glsl")),
                    new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/DeferredRendering/GBuffer/fragment.glsl")));

            lightingProgram = new ShaderProgram(
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/DeferredRendering/Lighting/compute.glsl")));

            skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/SkyBox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/SkyBox/fragment.glsl")));

            gBufferData = new BufferObject();
            gBufferData.ImmutableAllocate(sizeof(GLSLGBufferData), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);

            framebuffer = new Framebuffer();

            IsWireframe = false;
            IsSSAO = true;
            IsSSR = false;
            IsVolumetricLighting = true;
            IsVariableRateShading = false;

            NormalRayOffset = 1.0f;
            MaxSamples = 8;
            GIBoost = 2.0f;
            GISkyBoxBoost = 1.0f / GIBoost;
            StepMultiplier = 0.15f;
            IsVXGI = false;

            SetSize(width, height);
        }

        public void Render(ModelSystem modelSystem, LightManager lightManager)
        {
            GL.Viewport(0, 0, Result.Width, Result.Height);

            if (IsVariableRateShading)
            {
                ShadingRateClassifier.IsEnabled = true;
            }

            Voxelizer.ResultVoxelsAlbedo.BindToUnit(1);

            {
                framebuffer.Bind();
                framebuffer.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                if (IsWireframe)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                }
                
                gBufferProgram.Use();
                modelSystem.Draw();
                GL.Flush();
                
                if (IsWireframe)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }
                
                if (IsSSAO)
                {
                    SSAO.Compute();
                }

                Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
                if (IsSSAO) SSAO.Result.BindToUnit(0); else Texture.UnbindFromUnit(0);

                lightingProgram.Use();
                GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

                if (IsVariableRateShading || ShadingRateClassifier.DebugValue != ShadingRateClassifier.DebugMode.NoDebug)
                {
                    ShadingRateClassifier.Compute(Result);
                }

                GL.Disable(EnableCap.CullFace);
                GL.DepthFunc(DepthFunction.Lequal);

                skyBoxProgram.Use();
                GL.DrawArrays(PrimitiveType.Quads, 0, 24);

                GL.Enable(EnableCap.CullFace);
                GL.DepthFunc(DepthFunction.Less);
                GL.DepthMask(true);

                lightManager.Draw();
            }
            ShadingRateClassifier.IsEnabled = false;

            if (IsVolumetricLighting)
                VolumetricLight.Compute();

            if (IsSSR)
                SSR.Compute(Result);
        }

        private void DisposeBindlessTextures()
        {
            if (albedoAlphaTexture != null) { Texture.UnmakeTextureHandleResidentARB(glslGBufferData.AlbedoAlpha); albedoAlphaTexture.Dispose(); }
            if (normalSpecularTexture != null) { Texture.UnmakeTextureHandleResidentARB(glslGBufferData.NormalSpecular); normalSpecularTexture.Dispose(); }
            if (emissiveRoughnessTexture != null) { Texture.UnmakeTextureHandleResidentARB(glslGBufferData.EmissiveRoughness); emissiveRoughnessTexture.Dispose(); }
            if (velocityTexture != null) { Texture.UnmakeTextureHandleResidentARB(glslGBufferData.Velocity); velocityTexture.Dispose(); }
            if (depthTexture != null) { Texture.UnmakeTextureHandleResidentARB(glslGBufferData.Depth); depthTexture.Dispose(); }
        }

        public unsafe void SetSize(int width, int height)
        {
            SSAO.SetSize(width, height);
            SSR.SetSize(width, height);
            VolumetricLight.SetSize(width, height);
            ShadingRateClassifier.SetSize(width, height);

            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            DisposeBindlessTextures();

            albedoAlphaTexture = new Texture(TextureTarget2d.Texture2D);
            albedoAlphaTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            albedoAlphaTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            albedoAlphaTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba8);
            glslGBufferData.AlbedoAlpha = albedoAlphaTexture.GenTextureHandleARB();

            normalSpecularTexture = new Texture(TextureTarget2d.Texture2D);
            normalSpecularTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            normalSpecularTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            normalSpecularTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba8Snorm);
            glslGBufferData.NormalSpecular = normalSpecularTexture.GenTextureHandleARB();

            emissiveRoughnessTexture = new Texture(TextureTarget2d.Texture2D);
            emissiveRoughnessTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            emissiveRoughnessTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            emissiveRoughnessTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);
            glslGBufferData.EmissiveRoughness = emissiveRoughnessTexture.GenTextureHandleARB();

            velocityTexture = new Texture(TextureTarget2d.Texture2D);
            velocityTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            velocityTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            velocityTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rg16f);
            glslGBufferData.Velocity = velocityTexture.GenTextureHandleARB();

            depthTexture = new Texture(TextureTarget2d.Texture2D);
            depthTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            depthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            depthTexture.ImmutableAllocate(width, height, 1, (SizedInternalFormat)PixelInternalFormat.DepthComponent24);
            glslGBufferData.Depth = depthTexture.GenTextureHandleARB();

            gBufferData.SubData(0, sizeof(GLSLGBufferData), glslGBufferData);

            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, Result);
            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, albedoAlphaTexture);
            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, normalSpecularTexture);
            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment3, emissiveRoughnessTexture);
            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment4, velocityTexture);
            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, depthTexture);

            framebuffer.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3, DrawBuffersEnum.ColorAttachment4 });
        }

        public void Dispose()
        {
            SSAO.Dispose();
            SSR.Dispose();
            VolumetricLight.Dispose();
            ShadingRateClassifier.Dispose();
            Voxelizer.Dispose();

            Result.Dispose();
            DisposeBindlessTextures();

            gBufferProgram.Dispose();
            lightingProgram.Dispose();
            skyBoxProgram.Dispose();

            gBufferData.Dispose();
            framebuffer.Dispose();
        }
    }
}
