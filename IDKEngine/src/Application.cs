using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render;
using IDKEngine.Render.Objects;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

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
                    if (RasterizerPipeline != null) RasterizerPipeline.SetSize(RenderResolution.X, RenderResolution.Y, RenderPresentationResolution.X, RenderPresentationResolution.Y);
                }

                if (RenderMode == RenderMode.PathTracer)
                {
                    if (PathTracer != null) PathTracer.SetSize(RenderResolution.X, RenderResolution.Y);
                }

                if (RenderMode == RenderMode.Rasterizer || RenderMode == RenderMode.PathTracer)
                {
                    if (VolumetricLight != null) VolumetricLight.SetSize(RenderPresentationResolution.X, RenderPresentationResolution.Y);
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
                if (PathTracer != null) { PathTracer.Dispose(); PathTracer = null; }

                if (value == RenderMode.Rasterizer)
                {
                    RasterizerPipeline = new RasterPipeline(RenderResolution.X, RenderResolution.Y, RenderPresentationResolution.X, RenderPresentationResolution.Y);
                }

                if (value == RenderMode.PathTracer)
                {
                    PathTracer = new PathTracer(RenderResolution.X, RenderResolution.Y);
                }

                _renderMode = value;
            }
        }

        public Vector2i RenderResolution => new Vector2i((int)(RenderPresentationResolution.X * ResolutionScale), (int)(RenderPresentationResolution.Y * ResolutionScale));
        public bool RenderGui { get; private set; }
        public int FPS { get; private set; }

        public bool IsBloom = true;
        public bool IsVolumetricLighting = true;

        public struct CameraCollisionDetection
        {
            public bool IsEnabled;
            public int TestSteps;
            public int ResponseSteps;
            public float EpsilonNormalOffset;
        }

        public CameraCollisionDetection CamCollisionSettings = new CameraCollisionDetection()
        {
            IsEnabled = false,
            TestSteps = 3,
            ResponseSteps = 12,
            EpsilonNormalOffset = 0.001f
        };

        public bool GravityEnabled = false;
        public float GravityDownForce = 70.0f;

        private int fpsCounter;
        private readonly Stopwatch fpsTimer = Stopwatch.StartNew();
        protected override unsafe void OnRender(float dT)
        {
            Update(dT);
            if (RenderMode == RenderMode.Rasterizer)
            {
                if (RasterizerPipeline.IsConfigureGrid)
                {
                    RasterizerPipeline.Render(ModelSystem, LightManager, Camera, dT);
                    TonemapAndGamma.Combine(RasterizerPipeline.Result);
                    BoxRenderer.Render(TonemapAndGamma.Result, GpuBasicData.ProjView, new Box(RasterizerPipeline.Voxelizer.GridMin, RasterizerPipeline.Voxelizer.GridMax));
                }
                else
                {
                    RasterizerPipeline.Render(ModelSystem, LightManager, Camera, dT);

                    if (IsBloom)
                    {
                        Bloom.Compute(RasterizerPipeline.Result);
                    }

                    if (IsVolumetricLighting)
                    {
                        VolumetricLight.Compute();
                    }

                    TonemapAndGamma.Combine(RasterizerPipeline.Result, IsBloom ? Bloom.Result : null, IsVolumetricLighting ? VolumetricLight.Result : null);
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

            if (gui.SelectedEntity.Type != Gui.EntityType.None)
            {
                Box box = new Box();
                if (gui.SelectedEntity.Type == Gui.EntityType.Mesh)
                {
                    GpuBlasNode node = ModelSystem.BVH.Tlas.Blases[gui.SelectedEntity.Index].Root;
                    box.Min = node.Min;
                    box.Max = node.Max;

                    GpuDrawElementsCmd cmd = ModelSystem.DrawCommands[gui.SelectedEntity.Index];
                    box.Transform(ModelSystem.MeshInstances[cmd.BaseInstance + gui.SelectedEntity.Instance].ModelMatrix);
                }
                else
                {
                    LightManager.TryGetLight(gui.SelectedEntity.Index, out GpuLightWrapper abstractLight);
                    ref GpuLight light = ref abstractLight.GpuLight;

                    box.Min = new Vector3(light.Position) - new Vector3(light.Radius);
                    box.Max = new Vector3(light.Position) + new Vector3(light.Radius);
                }

                BoxRenderer.Render(TonemapAndGamma.Result, GpuBasicData.ProjView, box);
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
            MainThreadQueue.Execute();

            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FPS = fpsCounter;
                WindowTitle = $"IDKEngine FPS: {FPS}";
                fpsCounter = 0;
                fpsTimer.Restart();
            }

            //if (LightManager.TryGetLight(0, out Light light))
            //{
            //    light.GpuLight.Position.X += (MathF.Sin(GpuBasicData.Time) * 8.0f) * dT;
            //    light.GpuLight.Position.Z += (MathF.Cos(GpuBasicData.Time) * 4.0f) * dT;
            //}

            //for (int i = 0; i < LightManager.Count; i++)
            //{
            //    LightManager.TryGetLight(i, out Light light);

            //    Random rng = new Random(i);

            //    Vector3 pos = DebugLightPosXConst(light.GpuLight.Position.X, WindowTime, rng.NextSingle() + 1.0f, rng.NextSingle() + 1.0f, rng.NextSingle() * 8.0f + 2.0f, rng.NextSingle() * 6.0f + 2.0f);

            //    light.GpuLight.Position = pos;
            //    light.GpuLight.Color = Helper.VectorAbs(pos.Normalized()) * 8.5f;
            //}

            // Keyboard Inputs
            {
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
            }

            if (gui.FrameRecState != Gui.FrameRecorderState.Replaying)
            {
                if (KeyboardState[Keys.E] == InputState.Touched && !ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
                {
                    if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
                    {
                        MouseState.CursorMode = CursorModeValue.CursorNormal;
                        Camera.Velocity = Vector3.Zero;
                    }
                    else
                    {
                        MouseState.CursorMode = CursorModeValue.CursorDisabled;
                    }
                }

                if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
                {
                    Camera.ProcessInputs(KeyboardState, MouseState);
                    if (GravityEnabled)
                    {
                        Camera.ThisFrameAcceleration.Y += -GravityDownForce;
                    }

                    Camera.AdvanceSimulation(dT);
                }
            }

            gui.Update(this);

            //for (int i = 0; i < ModelSystem.VertexPositions.Length; i++)
            //{
            //    ModelSystem.VertexPositions[i].X += 1.0f * dT;
            //}
            //ModelSystem.UpdateVertexPositions(0, ModelSystem.VertexPositions.Length);
            //ModelSystem.BVH.BlasesRefit(0, ModelSystem.BVH.Tlas.Blases.Length);
            //ModelSystem.BVH.TlasBuild();

            if (CamCollisionSettings.IsEnabled)
            {
                for (int i = 0; i < CamCollisionSettings.ResponseSteps; i++)
                {
                    Sphere boundingVolume = new Sphere(Camera.PrevPosition, 0.5f);
                    Plane hitPlane;
                    float penetrationDepth;
                    bool hit = CollisionRoutine(ModelSystem, CamCollisionSettings, Camera.Position, &boundingVolume, &hitPlane, &penetrationDepth);
                    if (hit)
                    {
                        Vector3 newVelocity = Plane.Project(Camera.Velocity, hitPlane);
                        Camera.Velocity = newVelocity;

                        boundingVolume.Center += hitPlane.Normal * (penetrationDepth + CamCollisionSettings.EpsilonNormalOffset);
                        Camera.Position = boundingVolume.Center;

                        Camera.AdvanceSimulation(dT);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Updating global basicData Buffer
            {
                Camera.ProjectionSize = RenderResolution;

                GpuBasicData.PrevView = GpuBasicData.View;
                GpuBasicData.PrevProjView = GpuBasicData.ProjView;

                GpuBasicData.Projection = Camera.GetProjectionMatrix();
                GpuBasicData.InvProjection = GpuBasicData.Projection.Inverted();

                GpuBasicData.View = Camera.GetViewMatrix();
                GpuBasicData.InvView = GpuBasicData.View.Inverted();

                GpuBasicData.ProjView = GpuBasicData.View * GpuBasicData.Projection;
                GpuBasicData.InvProjView = GpuBasicData.ProjView.Inverted();

                GpuBasicData.CameraPos = Camera.Position;
                GpuBasicData.NearPlane = Camera.NearPlane;
                GpuBasicData.FarPlane = Camera.FarPlane;

                GpuBasicData.DeltaRenderTime = dT;
                GpuBasicData.Time = WindowTime;
                GpuBasicData.Frame++;

                basicDataBuffer.UploadElements(GpuBasicData);
            }

            LightManager.UpdateBufferData();

            bool anyMeshInstanceMoved = false;
            // Updating MeshInstance Buffer
            {
                ModelSystem.UpdateMeshInstanceBuffer(0, ModelSystem.MeshInstances.Length);
                for (int i = 0; i < ModelSystem.MeshInstances.Length; i++)
                {
                    if (ModelSystem.MeshInstances[i].DidMove())
                    {
                        ModelSystem.MeshInstances[i].SetPrevToCurrentMatrix();
                        anyMeshInstanceMoved = true;
                    }
                }
            }

            // Resetting Path Tracer if necessary
            {
                bool cameraMoved = GpuBasicData.PrevProjView != GpuBasicData.ProjView;
                if ((RenderMode == RenderMode.PathTracer) && (cameraMoved || anyMeshInstanceMoved))
                {
                    PathTracer.ResetRenderProcess();
                }
            }
        }

        private static Vector3 DebugLightPosXConst(float x, float t, float speedY, float speedZ, float scalarY, float scalarZ)
        {
            return new Vector3(x, 7.0f + MathF.Sin(t * speedY) * scalarY, MathF.Cos(t * speedZ) * scalarZ);
        }

        private Gui gui;
        private ShaderProgram finalProgram;

        public Camera Camera;
        public ModelSystem ModelSystem;
        public FrameStateRecorder<FrameState> FrameRecorder;

        public VolumetricLighting VolumetricLight;
        public Bloom Bloom;
        public TonemapAndGammaCorrect TonemapAndGamma;
        public BoxRenderer BoxRenderer;

        public LightManager LightManager;

        public RasterPipeline RasterizerPipeline;
        public PathTracer PathTracer;

        private TypedBuffer<GpuBasicData> basicDataBuffer;
        public GpuBasicData GpuBasicData;
        protected override unsafe void OnStart()
        {
            Logger.Log(Logger.LogLevel.Info, $"API: {Helper.API}");
            Logger.Log(Logger.LogLevel.Info, $"GPU: {Helper.GPU}");

            if (Helper.APIVersion < 4.6)
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support OpenGL 4.6. Press Enter to exit");
                Environment.Exit(0);
            }
            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support GL_ARB_bindless_texture. Press Enter to exit");
                Environment.Exit(0);
            }

            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageCallback(Helper.GLDebugCallbackFuncPtr, IntPtr.Zero);
            GL.PointSize(1.3f);
            Helper.SetDepthConvention(Helper.DepthConvention.ZeroToOne);
            GL.Disable(EnableCap.Multisample);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            basicDataBuffer = new TypedBuffer<GpuBasicData>();
            basicDataBuffer.ImmutableAllocate(BufferObject.BufferStorageFlag.DynamicStorage, 1);
            basicDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 0);

            finalProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/fragment.glsl")));
            Camera = new Camera(RenderResolution, new Vector3(7.63f, 2.71f, 0.8f), -165.4f, 7.4f);
            //Camera = new Camera(RenderResolution, new Vector3(-0.824f, 2.587f, -6.370f), -90.0f, 0.0f);
            //camera = new Camera(RenderResolution, new Vector3(-8.0f, 2.00f, -0.5f), -183.5f, 0.5f, 0.1f, 0.25f);

            SkyBoxManager.Init(new string[]
            {
                "res/textures/environmentMap/posx.jpg",
                "res/textures/environmentMap/negx.jpg",
                "res/textures/environmentMap/posy.jpg",
                "res/textures/environmentMap/negy.jpg",
                "res/textures/environmentMap/posz.jpg",
                "res/textures/environmentMap/negz.jpg"
            });

            RenderPresentationResolution = WindowFramebufferSize;
            VolumetricLight = new VolumetricLighting(RenderPresentationResolution.X, RenderPresentationResolution.Y, 7, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));
            Bloom = new Bloom(RenderPresentationResolution.X, RenderPresentationResolution.Y, 1.0f, 3.0f);
            TonemapAndGamma = new TonemapAndGammaCorrect(RenderPresentationResolution.X, RenderPresentationResolution.Y);
            BoxRenderer = new BoxRenderer();

            LightManager = new LightManager(12, 12);
            ModelSystem = new ModelSystem();

            if (true)
            {
                ModelLoader.Model sponza = ModelLoader.GltfToEngineFormat("res/models/Sponza/glTF/Sponza.gltf", Matrix4.CreateScale(1.815f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f));
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

                ModelLoader.Model lucy = ModelLoader.GltfToEngineFormat("res/models/Lucy/Lucy.gltf", Matrix4.CreateScale(0.8f) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(90.0f)) * Matrix4.CreateTranslation(-1.68f, 2.3f, 0.0f));
                lucy.Meshes[0].SpecularBias = -1.0f;
                lucy.Meshes[0].TransmissionBias = 0.98f;
                lucy.Meshes[0].IORBias = 0.174f;
                lucy.Meshes[0].AbsorbanceBias = new Vector3(0.81f, 0.18f, 0.0f);
                lucy.Meshes[0].RoughnessBias = -1.0f;

                ModelLoader.Model helmet = ModelLoader.GltfToEngineFormat("res/models/Helmet/Helmet.gltf", Matrix4.CreateRotationY(MathF.PI / 4.0f));

                //ModelLoader.Model emissiveTest = ModelLoader.GltfToEngineFormat("C:\\Users\\Julian\\Downloads\\Models\\glTF-Sample-Models\\2.0\\EmissiveStrengthTest\\glTF\\EmissiveStrengthTest.gltf", Matrix4.CreateRotationY(MathHelper.DegreesToRadians(-50.0f)) * Matrix4.CreateTranslation(10.0f, 2.0f, -9.0f));
                //ModelLoader.Model attenuationTest = ModelLoader.GltfToEngineFormat("C:\\Users\\Julian\\Downloads\\Models\\glTF-Sample-Models\\2.0\\AttenuationTest\\glTF\\AttenuationTest.gltf");
                //ModelLoader.Model transmissionRoughnessTest = ModelLoader.GltfToEngineFormat("C:\\Users\\Julian\\Downloads\\Models\\glTF-Sample-Models\\2.0\\TransmissionRoughnessTest\\glTF\\TransmissionRoughnessTest.gltf", Matrix4.CreateScale(10.0f));
                //ModelLoader.Model dragonAttenuation = ModelLoader.GltfToEngineFormat("C:\\Users\\Julian\\Downloads\\Models\\glTF-Sample-Models\\2.0\\DragonAttenuation\\glTF\\DragonAttenuation.gltf");
                //ModelLoader.Model transmissionTest = ModelLoader.GltfToEngineFormat("C:\\Users\\Julian\\Downloads\\Models\\glTF-Sample-Models\\2.0\\TransmissionTest\\glTF\\TransmissionTest.gltf", Matrix4.CreateScale(10.0f));
                //ModelLoader.Model aBeautifulGame = ModelLoader.GltfToEngineFormat("C:\\Users\\Julian\\Downloads\\Models\\glTF-Sample-Models\\2.0\\ABeautifulGame\\glTF\\ABeautifulGame.gltf", Matrix4.CreateScale(10.0f));
                //ModelSystem.Add(aBeautifulGame);

                ModelSystem.Add(sponza, lucy, helmet);

                LightManager.AddLight(new GpuLightWrapper(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(3.5f, 0.8f, 0.9f) * 6.3f, 0.3f));
                LightManager.AddLight(new GpuLightWrapper(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 0.3f));
                LightManager.AddLight(new GpuLightWrapper(new Vector3(4.5f, 5.7f, -2.0f), new Vector3(0.5f, 0.8f, 3.9f) * 6.3f, 0.3f));

                for (int i = 0; i < 3; i++)
                {
                    PointShadow pointShadow = new PointShadow(512, 0.5f, 60.0f);
                    LightManager.CreatePointShadowForLight(pointShadow, i);
                }

                //for (int i = 0; i < 128; i++)
                //{
                //    Light light = new Light(0.2f);
                //    light.GpuLight.Position.X = Helper.RandomFloat(-13.0f, 13.0f);
                //    LightManager.AddLight(light);

                //    PointShadow pointShadow = new PointShadow(256, 0.5f, 60.0f);
                //    LightManager.CreatePointShadowForLight(pointShadow, i);
                //}

                //LightManager.AddLight(new GpuLightWrapper(new Vector3(-12.25f, 7.8f, 0.3f), new Vector3(50.4f, 35.8f, 25.2f) * 0.6f, 1.0f)); // alt Color: new Vector3(50.4f, 35.8f, 25.2f)
                //LightManager.CreatePointShadowForLight(new PointShadow(512, 0.5f, 60.0f), LightManager.Count - 1);

                RenderMode = RenderMode.Rasterizer;
            }
            else
            {
                ModelLoader.Model a = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Base\NewSponza_Main_glTF_002.gltf");
                a.MeshInstances[28].ModelMatrix = Matrix4.CreateTranslation(-1000.0f, 0.0f, 0.0f);
                a.MeshInstances[89].ModelMatrix = Matrix4.CreateTranslation(-1000.0f, 0.0f, 0.0f);
                a.MeshInstances[271].ModelMatrix = Matrix4.CreateTranslation(-1000.0f, 0.0f, 0.0f);
                a.Meshes[288].SpecularBias = 1.0f;
                a.Meshes[288].RoughnessBias = -0.64f;
                a.Meshes[288].NormalMapStrength = 0.0f;
                a.Meshes[272].SpecularBias = 1.0f;
                a.Meshes[272].RoughnessBias = -0.82f;
                a.Meshes[93].EmissiveBias = 20.0f;
                a.Meshes[96].EmissiveBias = 20.0f;
                a.Meshes[99].EmissiveBias = 20.0f;
                a.Meshes[102].EmissiveBias = 20.0f;
                a.Meshes[105].EmissiveBias = 20.0f;
                a.Meshes[108].EmissiveBias = 20.0f;
                a.Meshes[111].EmissiveBias = 20.0f;
                a.Meshes[246].EmissiveBias = 20.0f;
                a.Meshes[291].EmissiveBias = 20.0f;
                a.Meshes[294].EmissiveBias = 20.0f;
                a.Meshes[297].EmissiveBias = 20.0f;
                a.Meshes[300].EmissiveBias = 20.0f;
                a.Meshes[303].EmissiveBias = 20.0f;
                a.Meshes[306].EmissiveBias = 20.0f;
                a.Meshes[312].EmissiveBias = 20.0f;
                a.Meshes[315].EmissiveBias = 20.0f;
                a.Meshes[318].EmissiveBias = 20.0f;
                a.Meshes[321].EmissiveBias = 20.0f;
                a.Meshes[324].EmissiveBias = 20.0f;
                a.Meshes[376].EmissiveBias = 20.0f;
                a.Meshes[379].EmissiveBias = 20.0f;
                ModelLoader.Model b = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Curtains\NewSponza_Curtains_glTF.gltf");
                ModelLoader.Model c = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Ivy\NewSponza_IvyGrowth_glTF.gltf");
                ModelLoader.Model d = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Tree\NewSponza_CypressTree_glTF.gltf");
                //ModelLoader.Model e = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Candles\NewSponza_4_Combined_glTF.gltf");
                ModelSystem.Add(a, b, c, d);

                //LightManager.AddLight(new Light(new Vector3(-6.256f, 8.415f, -0.315f), new Vector3(30.46f, 25.17f, 25.75f), 0.3f));
                //LightManager.CreatePointShadowForLight(new PointShadow(512, 0.1f, 60.0f), 0);

                RenderMode = RenderMode.Rasterizer;
                RasterizerPipeline.IsVXGI = false;
                RasterizerPipeline.Voxelizer.GridMin = new Vector3(-18.0f, -1.2f, -11.9f);
                RasterizerPipeline.Voxelizer.GridMax = new Vector3(21.3f, 19.7f, 17.8f);
                RasterizerPipeline.ConeTracer.MaxSamples = 4;

                VolumetricLight.Strength = 10.0f;
            }

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

        // We need to use raw pointers here instead of ref & out, because
        // "CS1628 - Cannot use in ref or out parameter inside an anonymous method, lambda expression, or query expression."
        private static unsafe bool CollisionRoutine(ModelSystem modelSystem, in CameraCollisionDetection settings, in Vector3 newPos, Sphere* previousPos, Plane* outHitPlane, float* outPenetrationDepth)
        {
            *outPenetrationDepth = float.MinValue;
            *outHitPlane = new Plane();

            Vector3 cameraStepSize = (newPos - previousPos->Center) / settings.TestSteps;
            for (int i = 1; i <= settings.TestSteps; i++)
            {
                previousPos->Center += cameraStepSize;
                Box playerBox = new Box(previousPos->Center - new Vector3(previousPos->Radius), previousPos->Center + new Vector3(previousPos->Radius));

                float biggestCosTheta = 0.0f;
                modelSystem.BVH.Intersect(playerBox, (in BVH.PrimitiveHitInfo hitInfo) =>
                {
                    Triangle triangle = new Triangle(
                        modelSystem.VertexPositions[hitInfo.TriangleIndices.X],
                        modelSystem.VertexPositions[hitInfo.TriangleIndices.Y],
                        modelSystem.VertexPositions[hitInfo.TriangleIndices.Z]
                    );
                    Matrix4 modelMatrix = modelSystem.MeshInstances[hitInfo.InstanceID].ModelMatrix;
                    Triangle worldSpaceTri = Triangle.Transformed(triangle, modelMatrix);

                    Vector3 closestPointOnTri = Intersections.TriangleClosestPoint(worldSpaceTri, previousPos->Center);
                    float distance = Vector3.Distance(closestPointOnTri, previousPos->Center);
                    float thisPenetrationDepth = previousPos->Radius - distance;
                    if (thisPenetrationDepth > 0.0f)
                    {
                        Plane thisHitPlane = new Plane(worldSpaceTri.Normal);

                        Vector3 hitPointToCameraDir = (previousPos->Center - closestPointOnTri) / distance;
                        float thisCosTheta = Vector3.Dot(thisHitPlane.Normal, hitPointToCameraDir);
                        if (thisCosTheta < 0.0f)
                        {
                            thisHitPlane.Normal *= -1.0f;
                        }

                        if (MathF.Abs(thisCosTheta) > MathF.Abs(biggestCosTheta))
                        {
                            biggestCosTheta = thisCosTheta;
                            *outHitPlane = thisHitPlane;
                            *outPenetrationDepth = thisPenetrationDepth;
                        }
                    }
                });

                if (*outPenetrationDepth != float.MinValue)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
