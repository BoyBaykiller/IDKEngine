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

    public enum TemporalAntiAliasingTechnique : int
    {
        None,
        TAA,
        FSR2,
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

        private float _resolutionScale = 1.0f;
        public float ResolutionScale
        {
            get => _resolutionScale;

            set
            { 
                _resolutionScale = value;
                RenderPresentationResolution = RenderPresentationResolution;
            }
        }

        private Vector2i _renderPresentationResolution;
        public Vector2i RenderPresentationResolution
        {
            get => _renderPresentationResolution;

            set
            {
                _renderPresentationResolution = value;
                
                if (RenderMode == RenderMode.Rasterizer)
                {
                    if (RasterizerPipeline != null) RasterizerPipeline.SetSize(RenderResolution.X, RenderResolution.Y);

                    if (TaaResolve != null) TaaResolve.SetSize(RenderPresentationResolution.X, RenderPresentationResolution.Y);
                    if (FSR2Wrapper != null) FSR2Wrapper.SetSize(RenderPresentationResolution.X, RenderPresentationResolution.Y, RenderResolution.X, RenderResolution.Y);
                }

                if (RenderMode == RenderMode.PathTracer)
                {
                    if (PathTracer != null) PathTracer.SetSize(RenderResolution.X, RenderResolution.Y);
                }

                if (RenderMode == RenderMode.Rasterizer || RenderMode == RenderMode.PathTracer)
                {
                    if (Bloom != null) Bloom.SetSize(RenderPresentationResolution.X, RenderPresentationResolution.Y);
                    if (TonemapAndGamma != null) TonemapAndGamma.SetSize(RenderPresentationResolution.X, RenderPresentationResolution.Y);
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

        public Vector2i RenderResolution => new Vector2i((int)(RenderPresentationResolution.X * ResolutionScale), (int)(RenderPresentationResolution.Y * ResolutionScale));
        public bool RenderGui { get; private set; }
        public int FPS { get; private set; }

        public bool IsBloom = true;
        public bool IsShadows = true;

        public TemporalAntiAliasingTechnique TemporalAntiAliasingTechnique = TemporalAntiAliasingTechnique.TAA;
        public int TAASamples = 6;

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

                    if (TemporalAntiAliasingTechnique == TemporalAntiAliasingTechnique.None)
                    {
                        if (IsBloom)
                        {
                            Bloom.Compute(RasterizerPipeline.Result);
                        }
                        TonemapAndGamma.Combine(RasterizerPipeline.Result, IsBloom ? Bloom.Result : null);
                    }
                    else if (TemporalAntiAliasingTechnique == TemporalAntiAliasingTechnique.TAA)
                    {
                        TaaResolve.RunTAA(RasterizerPipeline.Result);
                        if (IsBloom)
                        {
                            Bloom.Compute(TaaResolve.Result);
                        }
                        TonemapAndGamma.Combine(TaaResolve.Result, IsBloom ? Bloom.Result : null);
                    }
                    else if (TemporalAntiAliasingTechnique == TemporalAntiAliasingTechnique.FSR2)
                    {
                        FSR2Wrapper.RunFSR2(glslTaaData.Jitter, RasterizerPipeline.Result, RasterizerPipeline.DepthTexture, RasterizerPipeline.VelocityTexture, dT * 1000.0f, NEAR_PLANE, FAR_PLANE, CAMERA_FOV_Y);
                        if (IsBloom)
                        {
                            Bloom.Compute(FSR2Wrapper.Result);
                        }
                        TonemapAndGamma.Combine(FSR2Wrapper.Result, IsBloom ? Bloom.Result : null);

                        // TODO: This is a hack to fix global UBO bindings modified by FSR2
                        taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);
                        SkyBoxManager.skyBoxTextureBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 4);
                        RasterizerPipeline.Voxelizer.voxelizerDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 5);
                        RasterizerPipeline.gBufferData.BindBufferBase(BufferRangeTarget.UniformBuffer, 6);
                    }
                    RasterizerPipeline.LightingVRS.DebugRender(TonemapAndGamma.Result);

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
                    RenderPresentationResolution = new Vector2i(WindowFramebufferSize.X, WindowFramebufferSize.Y);
                }
            }
            if (KeyboardState[Keys.F11] == InputState.Touched)
            {
                WindowFullscreen = !WindowFullscreen;
            }

            if (TemporalAntiAliasingTechnique == TemporalAntiAliasingTechnique.None || TemporalAntiAliasingTechnique == TemporalAntiAliasingTechnique.TAA)
            {
                glslTaaData.MipmapBias = 0.0f;
            }
            if (TemporalAntiAliasingTechnique == TemporalAntiAliasingTechnique.TAA)
            {
                glslTaaData.Samples = TAASamples;
            }
            if (TemporalAntiAliasingTechnique == TemporalAntiAliasingTechnique.FSR2)
            {
                const float manualBias = 0.25f;
                glslTaaData.MipmapBias = FSR2Wrapper.GetRecommendedMipmapBias(RenderResolution.X, RenderPresentationResolution.X) + manualBias;
                glslTaaData.Samples = FSR2Wrapper.GetRecommendedSampleCount(RenderResolution.X, RenderPresentationResolution.X);
            }
            if (TemporalAntiAliasingTechnique != TemporalAntiAliasingTechnique.None)
            {
                Vector2 jitter = MyMath.GetHalton2D((int)GLSLBasicData.Frame % glslTaaData.Samples, 2, 3);
                glslTaaData.Jitter = (jitter * 2.0f - new Vector2(1.0f)) / RenderResolution;
            }
            taaDataBuffer.SubData(0, sizeof(GLSLTaaData), glslTaaData);

            GLSLBasicData.Projection = MyMath.CreatePerspectiveFieldOfViewDepthZeroToOne(CAMERA_FOV_Y, RenderPresentationResolution.X / (float)RenderPresentationResolution.Y, NEAR_PLANE, FAR_PLANE);
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
            GLSLBasicData.Frame++;
            basicDataBuffer.SubData(0, sizeof(GLSLBasicData), GLSLBasicData);

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

        private Gui gui;
        private ShaderProgram finalProgram;

        public Camera Camera;
        public ModelSystem ModelSystem;
        public BVH BVH;
        public FrameStateRecorder<FrameState> FrameRecorder;

        public Bloom Bloom;
        public TonemapAndGammaCorrecter TonemapAndGamma;
        public TAAResolve TaaResolve;
        public FSR2Wrapper FSR2Wrapper;
        public LightManager LightManager;
        public BoxRenderer MeshOutlineRenderer;

        public RasterPipeline RasterizerPipeline;
        public PathTracer PathTracer;

        private BufferObject basicDataBuffer;
        public GLSLBasicData GLSLBasicData;

        private BufferObject taaDataBuffer;
        private GLSLTaaData glslTaaData;
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
            Helper.SetDepthConvention(Helper.DepthConvention.ZeroToOne);
            GL.Disable(EnableCap.Multisample);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);

            RenderPresentationResolution = WindowFramebufferSize;
            RenderMode = RenderMode.Rasterizer;

            basicDataBuffer = new BufferObject();
            basicDataBuffer.ImmutableAllocate(sizeof(GLSLBasicData), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            basicDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 0);

            glslTaaData.Samples = 6;
            glslTaaData.Jitter = new Vector2(0.0f);
            taaDataBuffer = new BufferObject();
            taaDataBuffer.ImmutableAllocate(sizeof(GLSLTaaData), glslTaaData, BufferStorageFlags.DynamicStorageBit);
            taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

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

            LightManager = new LightManager(12, 12);
            MeshOutlineRenderer = new BoxRenderer();
            Bloom = new Bloom(RenderPresentationResolution.X, RenderPresentationResolution.Y, 1.0f, 3.0f);
            TonemapAndGamma = new TonemapAndGammaCorrecter(RenderPresentationResolution.X, RenderPresentationResolution.Y);
            TaaResolve = new TAAResolve(RenderPresentationResolution.X, RenderPresentationResolution.Y);
            FSR2Wrapper = new FSR2Wrapper(RenderPresentationResolution.X, RenderPresentationResolution.Y, RenderResolution.X, RenderResolution.Y);
            ModelSystem = new ModelSystem();

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
            sponza.Meshes[46].SpecularBias = 1.0f;
            sponza.Meshes[46].RoughnessBias = -0.436f;

            Model lucy = new Model("res/models/Lucy/Lucy.gltf", Matrix4.CreateRotationY(MathHelper.DegreesToRadians(90.0f)) * Matrix4.CreateScale(0.8f) * Matrix4.CreateTranslation(-1.68f, 2.3f, 0.0f));
            lucy.Meshes[0].SpecularBias = -1.0f;
            lucy.Meshes[0].RefractionChance = 0.98f;
            lucy.Meshes[0].IOR = 1.174f;
            lucy.Meshes[0].Absorbance = new Vector3(0.81f, 0.18f, 0.0f);
            lucy.Meshes[0].RoughnessBias = -1.0f;

            Model helmet = new Model("res/models/Helmet/Helmet.gltf");

            LightManager.AddLight(new Light(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(3.5f, 0.8f, 0.9f) * 6.3f, 0.3f));
            LightManager.AddLight(new Light(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 0.3f));
            LightManager.AddLight(new Light(new Vector3(4.5f, 5.7f, -2.0f), new Vector3(0.5f, 0.8f, 3.9f) * 6.3f, 0.3f));
            //LightManager.AddLight(new Light(new Vector3(-6.0f, 21.0f, 2.95f), new Vector3(1.0f) * 200.0f, 1.0f)); // alt Color: new Vector3(50.4f, 35.8f, 25.2f)
            //LightManager.CreatePointShadowForLight(new PointShadow(1536, 0.5f, 60.0f), LightManager.Count - 1);
            for (int i = 0; i < 3; i++)
            {
                PointShadow pointShadow = new PointShadow(512, 0.5f, 60.0f);
                LightManager.CreatePointShadowForLight(pointShadow, i);
            }
            ModelSystem.Add(sponza, lucy, helmet);

            //Model a = new Model(@"C:\Users\Julian\Downloads\Models\IntelSponza\Base\NewSponza_Main_Blender_glTF.gltf");
            //Model b = new Model(@"C:\Users\Julian\Downloads\Models\IntelSponza\Curtains\NewSponza_Curtains_glTF.gltf");
            //Model c = new Model(@"C:\Users\Julian\Downloads\Models\IntelSponza\Ivy\NewSponza_IvyGrowth_glTF.gltf");
            //LightManager.AddLight(new Light(new Vector3(2.002f, 18.693f, 3.117f), new Vector3(60.466f, 50.179f, 50.751f), 0.3f));
            //LightManager.CreatePointShadowForLight(new PointShadow(512, 1.0f, 60.0f), 0);
            //LightManager.AddLight(new Light(new Vector3(-5.947f, 3.31f, 2.256f), new Vector3(5.036f, 5.862f, 5.658f), 0.3f));
            //RasterizerPipeline.IsVXGI = true;
            //RasterizerPipeline.Voxelizer.GridMin = new Vector3(-18.0f, -1.2f, -11.9f);
            //RasterizerPipeline.Voxelizer.GridMax = new Vector3(21.3f, 19.7f, 17.8f);
            //ModelSystem.Add(a, b, c);

            BVH = new BVH(ModelSystem);

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
                RenderPresentationResolution = new Vector2i(WindowFramebufferSize.X, WindowFramebufferSize.Y);
            }
        }

        protected override void OnKeyPress(char key)
        {
            gui.Backend.PressChar(key);
        }
    }
}
