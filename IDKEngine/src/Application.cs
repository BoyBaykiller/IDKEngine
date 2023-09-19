using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    public enum RenderMode : int
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
        public const float NEAR_PLANE = 0.1f, FAR_PLANE = 500.0f;
        public static readonly float CAMERA_FOV_Y = MathHelper.DegreesToRadians(102.0f);

        public const float debugResScale = 1.0f;

        private Vector2i _renderResolution;
        public Vector2i RenderResolution
        {
            get => _renderResolution;

            set
            {
                _renderResolution = value;
                int width = _renderResolution.X;
                int height = _renderResolution.Y;

                if (RenderMode == RenderMode.Rasterizer)
                {
                    if (RasterizerPipeline != null) RasterizerPipeline.SetSize((int)(width * debugResScale), (int)(height * debugResScale));
                }

                if (RenderMode == RenderMode.PathTracer)
                {
                    if (PathTracer != null) PathTracer.SetSize(width, height);
                }

                if (RenderMode == RenderMode.Rasterizer || RenderMode == RenderMode.PathTracer)
                {
                    if (TaaResolve != null) TaaResolve.SetSize(width, height);
                    if (Bloom != null) Bloom.SetSize(width, height);
                    if (TonemapAndGamma != null) TonemapAndGamma.SetSize(width, height);
                }
            }
        }

        private RenderMode _renderMode;
        public RenderMode RenderMode
        {
            get => _renderMode;

            set
            {
                if (RasterizerPipeline != null) { RasterizerPipeline.Dispose(); RasterizerPipeline = null; }
                if (value == RenderMode.Rasterizer)
                {
                    RasterizerPipeline = new RasterPipeline(RenderResolution.X, RenderResolution.Y);
                }

                if (PathTracer != null) { PathTracer.Dispose(); PathTracer = null; }
                if (value == RenderMode.PathTracer)
                {
                    PathTracer = new PathTracer(BVH, RenderResolution.X, RenderResolution.Y);
                }

                _renderMode = value;
            }
        }

        public bool RenderGui { get; private set; }
        public int FPS { get; private set; }

        public bool IsBloom = true;
        public bool IsShadows = true;
        
        private int fpsCounter;
        private readonly Stopwatch fpsTimer = Stopwatch.StartNew();

        protected override unsafe void OnRender(float dT)
        {
            Update(dT);
            if (RenderMode == RenderMode.Rasterizer)
            {
                if (IsShadows)
                {
                    LightManager.RenderShadowMaps(ModelSystem);
                }

                if (RasterizerPipeline.IsConfigureGrid)
                {
                    RasterizerPipeline.Render(ModelSystem, GLSLBasicData.ProjView);
                    TonemapAndGamma.Combine(RasterizerPipeline.Result);
                    MeshOutlineRenderer.Render(TonemapAndGamma.Result, new Box(RasterizerPipeline.Voxelizer.GridMin, RasterizerPipeline.Voxelizer.GridMax));
                }
                else
                {
                    RasterizerPipeline.Render(ModelSystem, GLSLBasicData.ProjView, LightManager);

                    if (IsBloom)
                    {
                        Bloom.Compute(RasterizerPipeline.Result);
                    }

                    TaaResolve.RunTAAResolve(RasterizerPipeline.Result);
                    //TaaResolve.RunFSR2(RasterizerPipeline.Result, RasterizerPipeline.DepthTexture, RasterizerPipeline.VelocityTexture, dT * 1000.0f, NEAR_PLANE, FAR_PLANE, CAMERA_FOV_Y);
                    TonemapAndGamma.Combine(TaaResolve.Result, IsBloom ? Bloom.Result : null);
                    RasterizerPipeline.LightingVRS.DebugRender(TonemapAndGamma.Result);

                    // temp hack to fix global bindings modified by FSR2
                    TaaResolve.taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);
                    RasterizerPipeline.Voxelizer.voxelizerDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);
                    RasterizerPipeline.gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);
                }
            }
            else if (RenderMode == RenderMode.PathTracer)
            {
                PathTracer.Compute();

                if (IsBloom)
                {
                    Bloom.Compute(PathTracer.Result);
                }
                
                TonemapAndGamma.Combine(PathTracer.Result, IsBloom ? Bloom.Result : null);
            }

            if (gui.SelectedEntityType != Gui.EntityType.None)
            {
                Box box = new Box();
                if (gui.SelectedEntityType == Gui.EntityType.Mesh)
                {
                    GLSLBlasNode node = BVH.Tlas.BlasesInstances[gui.SelectedEntityIndex].Blas.Root;
                    box.Min = node.Min;
                    box.Max = node.Max;

                    GLSLDrawElementsCmd cmd = ModelSystem.DrawCommands[gui.SelectedEntityIndex];
                    box.Transform(ModelSystem.MeshInstances[cmd.BaseInstance].ModelMatrix);
                }
                else
                {
                    LightManager.TryGetLight(gui.SelectedEntityIndex, out Light abstractLight);
                    ref GLSLLight light = ref abstractLight.GLSLLight;
                    
                    box.Min = new Vector3(light.Position) - new Vector3(light.Radius);
                    box.Max = new Vector3(light.Position) + new Vector3(light.Radius);
                }

                MeshOutlineRenderer.Render(TonemapAndGamma.Result, box);
            }

            Framebuffer.Bind(0);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.Viewport(0, 0, WindowFramebufferSize.X, WindowFramebufferSize.Y);
            if (RenderGui)
            {
                gui.Draw(this, (float)dT);
            }
            else
            {
                TonemapAndGamma.Result.BindToUnit(0);
                finalProgram.Use();
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);

            fpsCounter++;
        }

        private unsafe void Update(float dT)
        {
            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FPS = fpsCounter;
                WindowTitle = $"FPS: {FPS}; Position {Camera.Position};";
                fpsCounter = 0;
                fpsTimer.Restart();
            }

            gui.Update(this, dT);

            if (KeyboardState[Keys.Escape] == InputState.Pressed)
            {
                ShouldClose();
            }
            if (KeyboardState[Keys.V] == InputState.Touched)
            {
                WindowVSync = !WindowVSync;
            }
            if (KeyboardState[Keys.G] == InputState.Touched)
            {
                RenderGui = !RenderGui;
                if (!RenderGui)
                {
                    RenderResolution = new Vector2i(WindowFramebufferSize.X, WindowFramebufferSize.Y);
                }
            }
            if (KeyboardState[Keys.F11] == InputState.Touched)
            {
                WindowFullscreen = !WindowFullscreen;
            }

            GLSLBasicData.Projection = MyMath.CreatePerspectiveFieldOfViewDepthZeroToOne(CAMERA_FOV_Y, RenderResolution.X / (float)RenderResolution.Y, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;
            GLSLBasicData.DeltaUpdate = dT;
            GLSLBasicData.PrevProjView = GLSLBasicData.ProjView;
            GLSLBasicData.PrevView = GLSLBasicData.View;
            GLSLBasicData.View = Camera.GenerateViewMatrix();
            GLSLBasicData.InvView = GLSLBasicData.View.Inverted();
            GLSLBasicData.ProjView = GLSLBasicData.View * GLSLBasicData.Projection;
            GLSLBasicData.InvProjView = GLSLBasicData.ProjView.Inverted();
            GLSLBasicData.CameraPos = Camera.Position;
            GLSLBasicData.Time = WindowTime;
            basicDataUBO.SubData(0, sizeof(GLSLBasicData), GLSLBasicData);

            ModelSystem.UpdateMeshInstanceBuffer(0, ModelSystem.MeshInstances.Length);
            bool anyMeshInstanceMoved = false;
            for (int i = 0; i < ModelSystem.MeshInstances.Length; i++)
            {
                if (ModelSystem.MeshInstances[i].DidMove())
                {
                    ModelSystem.MeshInstances[i].SetPrevToCurrentMatrix();
                    anyMeshInstanceMoved = true;
                }
            }

            if ((RenderMode == RenderMode.PathTracer) && ((GLSLBasicData.PrevProjView != GLSLBasicData.ProjView) || anyMeshInstanceMoved))
            {
                PathTracer.ResetRenderProcess();
            }
        }

        public Camera Camera;
        public ModelSystem ModelSystem;
        public BVH BVH;
        public GLSLBasicData GLSLBasicData;
        public FrameStateRecorder<FrameState> FrameRecorder;

        public Bloom Bloom;
        public TonemapAndGammaCorrecter TonemapAndGamma;
        public TAAResolve TaaResolve;
        public LightManager LightManager;
        public BoxRenderer MeshOutlineRenderer;

        public RasterPipeline RasterizerPipeline;
        public PathTracer PathTracer;

        private Gui gui;
        private BufferObject basicDataUBO;
        private ShaderProgram finalProgram;

        protected override unsafe void OnStart()
        {
            Logger.Log(Logger.LogLevel.Info, $"API: {Helper.API}");
            Logger.Log(Logger.LogLevel.Info, $"GPU: {Helper.GPU}");

            if (Helper.APIVersion < 4.6)
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support OpenGL 4.6. Press Enter to exit");
                Console.ReadLine();
                Environment.Exit(0);
            }
            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support GL_ARB_bindless_texture. Press Enter to exit");
                Console.ReadLine();
                Environment.Exit(0);
            }

            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageCallback(Helper.GLDebugCallbackFuncPtr, 0);
            GL.PointSize(1.3f);
            //GL.ClipControl(ClipOrigin.LowerLeft, ClipDepthMode.ZeroToOne);
            GL.Disable(EnableCap.Multisample);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

            RenderResolution = WindowFramebufferSize;

            basicDataUBO = new BufferObject();
            basicDataUBO.ImmutableAllocate(sizeof(GLSLBasicData), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            basicDataUBO.BindBufferBase(BufferRangeTarget.UniformBuffer, 0);

            finalProgram = new ShaderProgram(
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

            Model sponza = new Model("res/models/Sponza/glTF/Sponza.gltf", Matrix4.CreateScale(1.815f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f));

            // fix minor transparency issue with roughness
            sponza.Meshes[0].RoughnessBias = -1.0f;
            sponza.Meshes[1].RoughnessBias = -1.0f;
            sponza.Meshes[20].RoughnessBias = -1.0f;
            sponza.Meshes[53].RoughnessBias = -1.0f;
            sponza.Meshes[75].RoughnessBias = -1.0f;
            sponza.Meshes[77].RoughnessBias = -1.0f;
            sponza.Meshes[79].RoughnessBias = -1.0f;
            sponza.Meshes[81].RoughnessBias = -1.0f;
            sponza.Meshes[83].RoughnessBias = -1.0f;
            sponza.Meshes[85].RoughnessBias = -1.0f;
            sponza.Meshes[87].RoughnessBias = -1.0f;
            sponza.Meshes[89].RoughnessBias = -1.0f;
            sponza.Meshes[91].RoughnessBias = -1.0f;
            sponza.Meshes[93].RoughnessBias = -1.0f;

            sponza.Meshes[63].EmissiveBias = 10.0f;
            sponza.Meshes[70].EmissiveBias = 20.0f;
            sponza.Meshes[3].EmissiveBias = 12.0f;
            sponza.Meshes[99].EmissiveBias = 15.0f;
            sponza.Meshes[97].EmissiveBias = 9.0f;
            sponza.Meshes[42].EmissiveBias = 20.0f;
            sponza.Meshes[38].EmissiveBias = 20.0f;
            sponza.Meshes[40].EmissiveBias = 20.0f;
            sponza.Meshes[42].EmissiveBias = 20.0f;
            sponza.Meshes[46].SpecularBias = 1.0f; // floor
            sponza.Meshes[46].RoughnessBias = -0.436f; // floor

            Model lucy = new Model("res/models/Lucy/Lucy.gltf", Matrix4.CreateRotationY(MathHelper.DegreesToRadians(90.0f)) * Matrix4.CreateScale(0.8f) * Matrix4.CreateTranslation(-1.68f, 2.3f, 0.0f));
            lucy.Meshes[0].SpecularBias = -1.0f;
            lucy.Meshes[0].RefractionChance = 0.98f;
            lucy.Meshes[0].IOR = 1.174f;
            lucy.Meshes[0].Absorbance = new Vector3(0.81f, 0.18f, 0.0f);
            lucy.Meshes[0].RoughnessBias = -1.0f;

            Model helmet = new Model("res/models/Helmet/Helmet.gltf");

            //Model giPlayground = new Model("res/models/GIPlayground/GIPlayground.gltf");
            //Model cornellBox = new Model("res/models/CornellBox/scene.gltf");

            //Model a = new Model(@"C:\Users\Julian\Downloads\Models\IntelSponza\Base\NewSponza_Main_Blender_glTF.gltf");
            //Model b = new Model(@"C:\Users\Julian\Downloads\Models\IntelSponza\Curtains\NewSponza_Curtains_glTF.gltf");
            //Model c = new Model(@"C:\Users\Julian\Downloads\Models\IntelSponza\Ivy\NewSponza_IvyGrowth_glTF.gltf");
            //Model bistroExterior = new Model(@"C:\Users\Julian\Downloads\Models\BistroExterior\BistroExterior.gltf");
            //Model minecraft = new Model(@"C:\Users\Julian\Downloads\Models\Minecraft\Minecraft.gltf");

            ModelSystem = new ModelSystem();
            ModelSystem.Add(sponza, lucy, helmet);

            BVH = new BVH(ModelSystem);
            LightManager = new LightManager(12, 12);
            MeshOutlineRenderer = new BoxRenderer();
            Bloom = new Bloom(RenderResolution.X, RenderResolution.Y, 1.0f, 3.0f);
            TonemapAndGamma = new TonemapAndGammaCorrecter(RenderResolution.X, RenderResolution.Y);
            TaaResolve = new TAAResolve(RenderResolution.X, RenderResolution.Y);

            LightManager.AddLight(new Light(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(3.5f, 0.8f, 0.9f) * 6.3f, 0.3f));
            LightManager.AddLight(new Light(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 0.3f));
            LightManager.AddLight(new Light(new Vector3(4.5f, 5.7f, -2.0f), new Vector3(0.5f, 0.8f, 3.9f) * 6.3f, 0.3f));

            //LightManager.AddLight(new Light(new Vector3(-6.0f, 21.0f, 2.95f), new Vector3(1.0f) * 200.0f, 1.0f)); // alt Color: new Vector3(50.450, 35.840, 25.270)
            //LightManager.CreatePointShadowForLight(new PointShadow(1536, 0.5f, 60.0f), LightManager.Count - 1);

            for (int i = 0; i < 3; i++)
            {
                PointShadow pointShadow = new PointShadow(512, 0.5f, 60.0f);
                LightManager.CreatePointShadowForLight(pointShadow, i);
            }

            RenderMode = RenderMode.Rasterizer;
            RenderGui = true;
            WindowVSync = true;
            MouseState.CursorMode = CursorModeValue.CursorNormal;
            FrameRecorder = new FrameStateRecorder<FrameState>();
            gui = new Gui(WindowFramebufferSize.X, WindowFramebufferSize.Y);

            GC.Collect();
        }

        protected override void OnEnd()
        {

        }

        protected override void OnResize()
        {
            gui.Backend.SetSize(WindowFramebufferSize.X, WindowFramebufferSize.Y);

            // if we don't render to the screen via gui always make viewport match window size
            if (!RenderGui)
            {
                RenderResolution = new Vector2i(WindowFramebufferSize.X, WindowFramebufferSize.Y);
            }
        }

        protected override void OnKeyPress(char key)
        {
            gui.Backend.PressChar(key);
        }
    }
}
