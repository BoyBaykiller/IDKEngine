using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    public enum RenderMode
    {
        Rasterizer,
        VXGI_WIP,
        PathTracer
    }

    class Application : GameWindowBase
    {
        public Application(int width, int height, string title)
            : base(width, height, title, 4, 6)
        {

        }


        public const float EPSILON = 0.001f;
        public const float NEAR_PLANE = 0.01f, FAR_PLANE = 500.0f;

        public bool IsVolumetricLighting = true, IsSSAO = true, IsSSR = false, IsBloom = true, IsShadows = true, IsVRSForwardRender = false, IsWireframe = false;
        public int FPS;

        public Vector2i ViewportResolution { get; private set; }

        public bool RenderGui { get; private set; }
        private int fps;
        protected override unsafe void OnRender(float dT)
        {
            GLSLBasicData.DeltaUpdate = dT;
            GLSLBasicData.PrevProjView = GLSLBasicData.ProjView;
            GLSLBasicData.View = Camera.ViewMatrix;
            GLSLBasicData.InvView = GLSLBasicData.View.Inverted();
            GLSLBasicData.ProjView = GLSLBasicData.View * GLSLBasicData.Projection;
            GLSLBasicData.InvProjView = GLSLBasicData.ProjView.Inverted();
            GLSLBasicData.CameraPos = Camera.Position;
            GLSLBasicData.Time = WindowTime;
            if (GetRenderMode() == RenderMode.PathTracer && GLSLBasicData.PrevProjView != GLSLBasicData.ProjView)
            {
                PathTracer.ResetRender();
            }
            basicDataUBO.SubData(0, sizeof(GLSLBasicData), GLSLBasicData);

            if (GetRenderMode() == RenderMode.Rasterizer)
            {
                if (IsWireframe)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                }

                // Last frames SSAO
                if (IsSSAO)
                    SSAO.Compute(ForwardRenderer.DepthTexture, ForwardRenderer.NormalSpecTexture);

                if (IsShadows)
                {
                    pointShadowManager.UpdateShadowMaps(ModelSystem);
                    GL.ColorMask(true, true, true, true);
                }

                ModelSystem.FrustumCull(GLSLBasicData.ProjView);

                GL.Viewport(0, 0, ForwardRenderer.Result.Width, ForwardRenderer.Result.Height);

                if (IsVRSForwardRender)
                    ShadingRateClassifier.IsEnabled = true;

                ForwardRenderer.Render(ModelSystem, IsSSAO ? SSAO.Result : null);
                ShadingRateClassifier.IsEnabled = false;

                if (IsBloom)
                    Bloom.Compute(ForwardRenderer.Result);

                if (IsVolumetricLighting)
                    VolumetricLight.Compute(ForwardRenderer.DepthTexture);

                if (IsSSR)
                    SSR.Compute(ForwardRenderer.Result, ForwardRenderer.NormalSpecTexture, ForwardRenderer.DepthTexture);

                PostProcessor.Compute(ForwardRenderer.Result, IsBloom ? Bloom.Result : null, IsVolumetricLighting ? VolumetricLight.Result : null, IsSSR ? SSR.Result : null, ForwardRenderer.VelocityTexture, ForwardRenderer.DepthTexture);

                if (ShadingRateClassifier.HAS_VARIABLE_RATE_SHADING)
                {
                    if (IsVRSForwardRender)
                        ShadingRateClassifier.Compute(PostProcessor.Result, ForwardRenderer.VelocityTexture);
                }
                // Small "hack" to enable VRS debug image on systems that don't support the extension
                else if (ShadingRateClassifier.DebugValue != ShadingRateClassifier.DebugMode.NoDebug)
                {
                    ShadingRateClassifier.Compute(PostProcessor.Result, ForwardRenderer.VelocityTexture);
                }

                if (IsWireframe)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }
            }
            else if (GetRenderMode() == RenderMode.VXGI_WIP)
            {
                Voxelizer.Render(ModelSystem);
                Voxelizer.DebugRender(PostProcessor.Result);

                if (IsBloom)
                    Bloom.Compute(PostProcessor.Result);

                PostProcessor.Compute(PostProcessor.Result, IsBloom ? Bloom.Result : null, null, null, null, null);
            }
            else if (GetRenderMode() == RenderMode.PathTracer)
            {
                PathTracer.Compute();

                if (IsBloom)
                    Bloom.Compute(PathTracer.Result);

                PostProcessor.Compute(PathTracer.Result, IsBloom ? Bloom.Result : null, null, null, null, null);
            }

            GL.Viewport(0, 0, PostProcessor.Result.Width, PostProcessor.Result.Height);
            MeshOutlineRenderer.Render(PostProcessor.Result);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);

            GL.Viewport(0, 0, WindowSize.X, WindowSize.Y);
            Framebuffer.Bind(0);

            if (RenderGui)
            {
                gui.Draw(this, (float)dT);
            }
            else
            {
                PostProcessor.Result.BindToUnit(0);
                FinalProgram.Use();
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);

            fps++;
        }

        private readonly Stopwatch fpsTimer = Stopwatch.StartNew();
        protected override void OnUpdate(float dT)
        {
            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FPS = fps;
                WindowTitle = $"FPS: {FPS}; Position {Camera.Position};";
                fps = 0;
                fpsTimer.Restart();
            }

            if (KeyboardState[Keys.Escape] == InputState.Pressed)
                ShouldClose();

            if (KeyboardState[Keys.V] == InputState.Touched)
                WindowVSync = !WindowVSync;
            
            if (KeyboardState[Keys.G] == InputState.Touched)
            {
                RenderGui = !RenderGui;
                if (!RenderGui)
                {
                    SetViewportResolution(WindowSize.X, WindowSize.Y);
                }
            }

            if (KeyboardState[Keys.F11] == InputState.Touched)
                WindowFullscreen = !WindowFullscreen;

            gui.Update(this, dT);
        }

        public Camera Camera;
        private BufferObject basicDataUBO;
        private PointShadowManager pointShadowManager;
        public ShaderProgram FinalProgram;
        public ModelSystem ModelSystem;
        public BVH BVH;
        private Gui gui;
        public GLSLBasicData GLSLBasicData;
        public FrameStateRecorder<RecordableState> FrameRecorder;

        public ForwardRenderer ForwardRenderer;
        public VolumetricLighter VolumetricLight;
        public SSR SSR;
        public SSAO SSAO;
        public Bloom Bloom;
        public ShadingRateClassifier ShadingRateClassifier;
        public PostProcessor PostProcessor;
        public PathTracer PathTracer;
        public Voxelizer Voxelizer;
        public LightManager LightManager;
        public MeshOutlineRenderer MeshOutlineRenderer;
        protected override unsafe void OnStart()
        {
            Console.WriteLine($"API: {GL.GetString(StringName.Version)}");
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}\n\n");

            if (Helper.APIVersion < 4.6)
            {
                Console.WriteLine("Your system does not support OpenGL 4.6. Press Enter to exit");
                Console.ReadLine();
                Environment.Exit(1);
            }
            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
            {
                Console.WriteLine("Your system does not support GL_ARB_bindless_texture. Press Enter to exit");
                Console.ReadLine();
                Environment.Exit(1);
            }

            RenderGui = true;
            WindowVSync = true;
            ViewportResolution = WindowSize;
            MouseState.CursorMode = CursorModeValue.CursorNormal;

            GL.PointSize(1.3f);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
#if DEBUG
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageCallback(Helper.DebugCallback, 0);
#endif

            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), ViewportResolution.X / (float)ViewportResolution.Y, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;

            basicDataUBO = new BufferObject();
            basicDataUBO.ImmutableAllocate(sizeof(GLSLBasicData), GLSLBasicData, BufferStorageFlags.DynamicStorageBit);
            basicDataUBO.BindBufferBase(BufferRangeTarget.UniformBuffer, 0);

            FinalProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/fragment.glsl")));
            Camera = new Camera(new Vector3(7.63f, 2.71f, 0.8f), new Vector3(0.0f, 1.0f, 0.0f), -165.4f, 7.4f, 0.1f, 0.25f);
            //camera = new Camera(new Vector3(-8.0f, 2.00f, -0.5f), new Vector3(0.0f, 1.0f, 0.0f), -183.5f, 0.5f, 0.1f, 0.25f);

            SkyBoxManager.Init(new string[]
            {
                "res/Textures/EnvironmentMap/posx.jpg",
                "res/Textures/EnvironmentMap/negx.jpg",
                "res/Textures/EnvironmentMap/posy.jpg",
                "res/Textures/EnvironmentMap/negy.jpg",
                "res/Textures/EnvironmentMap/posz.jpg",
                "res/Textures/EnvironmentMap/negz.jpg"
            });

            Model sponza = new Model("res/models/OBJSponza/sponza.obj");
            for (int i = 0; i < sponza.ModelMatrices.Length; i++) // 0.0145f
                sponza.ModelMatrices[i][0] = Matrix4.CreateScale(5.0f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f);


            // fix transparency
            sponza.Meshes[0].RoughnessBias = -1.0f;
            sponza.Meshes[19].RoughnessBias = -1.0f;
            sponza.Meshes[2].RoughnessBias = -1.0f;

            sponza.Meshes[10].EmissiveBias = 11.0f;
            sponza.Meshes[8].SpecularBias = 1.0f;
            sponza.Meshes[8].RoughnessBias = -1.0f;
            sponza.Meshes[3].EmissiveBias = 2.67f;
            sponza.Meshes[17].RefractionChance = 1.0f;
            sponza.Meshes[17].RoughnessBias = -0.7f;

            Model lucy = new Model("res/models/Lucy/Lucy.gltf");
            lucy.ModelMatrices[0][0] = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(90.0f)) * Matrix4.CreateScale(0.8f) * Matrix4.CreateTranslation(-1.68f, 2.3f, 0.0f);
            lucy.Meshes[0].RefractionChance = 0.9f;
            lucy.Meshes[0].IOR = 1.174f;
            lucy.Meshes[0].Absorbance = new Vector3(0.81f, 0.18f, 0.0f);

            Model helmet = new Model("res/models/Helmet/Helmet.gltf");
            helmet.Meshes[0].SpecularBias = 1.0f;

            ModelSystem = new ModelSystem();
            ModelSystem.Add(new Model[] { sponza, lucy, helmet });

            BVH = new BVH(ModelSystem);

            LightManager  = new LightManager(12, 12);
            MeshOutlineRenderer = new MeshOutlineRenderer();
            Bloom = new Bloom(ViewportResolution.X, ViewportResolution.Y, 1.0f, 3.0f);
            PostProcessor = new PostProcessor(ViewportResolution.X, ViewportResolution.Y);

            List<GLSLLight> lights = new List<GLSLLight>();
            //lights.Add(new GLSLLight(new Vector3(-6.0f, 21.0f, 2.95f), new Vector3(4.585f, 4.725f, 2.56f) * 10.0f, 1.0f));
            lights.Add(new GLSLLight(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(3.5f, 0.8f, 0.9f) * 6.3f, 0.3f));
            lights.Add(new GLSLLight(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 0.3f));
            lights.Add(new GLSLLight(new Vector3(4.5f, 5.7f, -2.0f), new Vector3(0.5f, 0.8f, 3.9f) * 6.3f, 0.3f));
            LightManager.Add(CollectionsMarshal.AsSpan(lights));

            SetRenderMode(RenderMode.Rasterizer);

            FrameRecorder = new FrameStateRecorder<RecordableState>();
            gui = new Gui(WindowSize.X, WindowSize.Y);
        }

        protected override void OnResize()
        {
            gui.ImGuiBackend.WindowResized(WindowSize.X, WindowSize.Y);
            // if we don't render to the screen via gui always make viewport match window size

            if (!RenderGui)
            {
                SetViewportResolution(WindowSize.X, WindowSize.Y);
            }
        }

        public void SetViewportResolution(int width, int height)
        {
            if (width < 16 || height < 16)
                return;

            ViewportResolution = new Vector2i(width, height);

            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), width / (float)height, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;

            if (GetRenderMode() == RenderMode.Rasterizer)
            {
                ForwardRenderer.SetSize(width, height);
                VolumetricLight.SetSize(width, height);
                ShadingRateClassifier.SetSize(width, height);
                ForwardRenderer.SetSize(width, height);
                SSR.SetSize(width, height);
                SSAO.SetSize(width, height);
            }

            if (GetRenderMode() == RenderMode.PathTracer)
            {
                PathTracer.SetSize(width, height);
            }

            if (GetRenderMode() == RenderMode.Rasterizer || GetRenderMode() == RenderMode.VXGI_WIP || GetRenderMode() == RenderMode.PathTracer)
            {
                Bloom.SetSize(width, height);
                PostProcessor.SetSize(width, height);
            }
        }

        protected override void OnEnd()
        {

        }

        protected override void OnKeyPress(char key)
        {
            gui.ImGuiBackend.PressChar(key);
        }


        private RenderMode _renderMode;
        public RenderMode GetRenderMode()
        {
            return _renderMode;
        }

        public void SetRenderMode(RenderMode renderMode)
        {
            if (ShadingRateClassifier != null) { ShadingRateClassifier.Dispose(); ShadingRateClassifier = null; }
            if (SSAO != null) { SSAO.Dispose(); SSAO = null; }
            if (SSR != null) { SSR.Dispose(); SSR = null; }
            if (VolumetricLight != null) { VolumetricLight.Dispose(); VolumetricLight = null; }
            if (ForwardRenderer != null) { ForwardRenderer.Dispose(); ForwardRenderer = null; }
            if (pointShadowManager != null) { pointShadowManager.Dispose(); pointShadowManager = null; }
            if (renderMode == RenderMode.Rasterizer)
            {
                ShadingRateClassifier = new ShadingRateClassifier(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl")), ViewportResolution.X, ViewportResolution.Y);
                Span<NvShadingRateImage> shadingRates = stackalloc NvShadingRateImage[]
                {
                    NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                    NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                    NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                    NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                    NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
                };
                ShadingRateClassifier.SetShadingRatePaletteNV(shadingRates);
                ShadingRateClassifier.BindVRSNV(ShadingRateClassifier);

                SSAO = new SSAO(ViewportResolution.X, ViewportResolution.Y, 10, 0.1f, 2.0f);
                SSR = new SSR(ViewportResolution.X, ViewportResolution.Y, 30, 8, 50.0f);
                VolumetricLight = new VolumetricLighter(ViewportResolution.X, ViewportResolution.Y, 7, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));
                ForwardRenderer = new ForwardRenderer(LightManager, ViewportResolution.X, ViewportResolution.Y, 6);

                pointShadowManager = new PointShadowManager();
                for (int j = 0; j < 3; j++)
                {
                    PointShadow pointShadow = new PointShadow(LightManager, j, 512, 0.5f, 60.0f);
                    pointShadowManager.Add(pointShadow);
                }
            }

            if (Voxelizer != null) { Voxelizer.Dispose(); Voxelizer = null; }
            if (renderMode == RenderMode.VXGI_WIP)
            {
                int i = 0;
                ModelSystem.UpdateDrawCommandBuffer(0, ModelSystem.DrawCommands.Length, (ref GLSLDrawCommand cmd) =>
                {
                    cmd.InstanceCount = ModelSystem.Meshes[i++].InstanceCount;
                });
                Voxelizer = new Voxelizer(384, 384, 384, new Vector3(-28.0f, -3.0f, -17.0f), new Vector3(28.0f, 20.0f, 17.0f));
            }

            if (PathTracer != null) { PathTracer.Dispose(); PathTracer = null; }
            if (renderMode == RenderMode.PathTracer)
            {
                PathTracer = new PathTracer(BVH, ModelSystem, ViewportResolution.X, ViewportResolution.Y);
            }

            _renderMode = renderMode;
        }
    }
}
