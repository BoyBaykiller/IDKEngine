using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    public enum RenderMode
    {
        Rasterizer,
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

        public bool IsBloom = true, IsShadows = true;
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
                if (IsShadows)
                {
                    pointShadowManager.UpdateShadowMaps(ModelSystem);
                    GL.ColorMask(true, true, true, true);
                }

                if (RasterizerPipeline.IsDebugRenderVXGIGrid)
                {
                    RasterizerPipeline.Render(ModelSystem, GLSLBasicData.ProjView);
                    PostProcessor.Compute(RasterizerPipeline.Result, null, null, null, false);
                }
                else
                {
                    RasterizerPipeline.Render(ModelSystem, GLSLBasicData.ProjView, LightManager);

                    if (IsBloom)
                        Bloom.Compute(RasterizerPipeline.Result);

                    PostProcessor.Compute(RasterizerPipeline.Result, IsBloom ? Bloom.Result : null, RasterizerPipeline.IsVolumetricLighting ? RasterizerPipeline.VolumetricLight.Result : null, RasterizerPipeline.IsSSR ? RasterizerPipeline.SSR.Result : null, true);
                    RasterizerPipeline.ShadingRateClassifier.DebugRender(PostProcessor.Result);
                }

            }
            else if (GetRenderMode() == RenderMode.PathTracer)
            {
                PathTracer.Compute();

                if (IsBloom)
                    Bloom.Compute(PathTracer.Result);

                PostProcessor.Compute(PathTracer.Result, IsBloom ? Bloom.Result : null, null, null, false);
            }

            Framebuffer.Bind(0);

            MeshOutlineRenderer.Render(PostProcessor.Result);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);

            GL.Viewport(0, 0, WindowSize.X, WindowSize.Y);

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

            //Vector3 pos = ModelSystem.ModelMatrices[25][0].ExtractTranslation();
            //pos += new Vector3(0.0f, MathF.Sin(WindowTime * 1.1f) * 0.005f, 0.0f);
            //ModelSystem.ModelMatrices[25][0] = ModelSystem.ModelMatrices[25][0].ClearTranslation() * Matrix4.CreateTranslation(pos);
            //ModelSystem.UpdateModelMatricesBuffer(25, 26);

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

        public Bloom Bloom;
        public PostProcessor PostProcessor;
        public LightManager LightManager;
        public MeshOutlineRenderer MeshOutlineRenderer;

        public RasterPipeline RasterizerPipeline;
        public PathTracer PathTracer;
        protected override unsafe void OnStart()
        {
            Console.WriteLine($"API: {Helper.API}");
            Console.WriteLine($"GPU: {Helper.GPU}\n\n");

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

#if DEBUG
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageCallback(Helper.DebugCallback, 0);
#endif
            GL.PointSize(1.3f);
            GL.Disable(EnableCap.Multisample);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

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
                "res/textures/environmentMap/posx.jpg",
                "res/textures/environmentMap/negx.jpg",
                "res/textures/environmentMap/posy.jpg",
                "res/textures/environmentMap/negy.jpg",
                "res/textures/environmentMap/posz.jpg",
                "res/textures/environmentMap/negz.jpg"
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
                RasterizerPipeline.SetSize(width, height);
            }

            if (GetRenderMode() == RenderMode.PathTracer)
            {
                PathTracer.SetSize(width, height);
            }

            if (GetRenderMode() == RenderMode.Rasterizer || GetRenderMode() == RenderMode.PathTracer)
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
            if (pointShadowManager != null) { pointShadowManager.Dispose(); pointShadowManager = null; }
            if (RasterizerPipeline != null) { RasterizerPipeline.Dispose(); RasterizerPipeline = null; }
            if (renderMode == RenderMode.Rasterizer)
            {
                RasterizerPipeline = new RasterPipeline(ViewportResolution.X, ViewportResolution.Y);

                pointShadowManager = new PointShadowManager();
                for (int j = 0; j < 3; j++)
                {
                    PointShadow pointShadow = new PointShadow(LightManager, j, 512, 0.5f, 60.0f);
                    pointShadowManager.Add(pointShadow);
                }
            }

            if (PathTracer != null) { PathTracer.Dispose(); PathTracer = null; }
            if (renderMode == RenderMode.PathTracer)
            {
                PathTracer = new PathTracer(BVH, ViewportResolution.X, ViewportResolution.Y);
            }

            _renderMode = renderMode;
        }
    }
}
