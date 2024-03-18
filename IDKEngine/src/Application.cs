using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;
using IDKEngine.Windowing;
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

        private float _resolutionScale = 1.0f;
        public float ResolutionScale
        {
            get => _resolutionScale;

            set
            {
                _resolutionScale = value;
                PresentationResolution = PresentationResolution;
            }
        }

        private Vector2i _presentationResolution;
        public Vector2i PresentationResolution
        {
            get => _presentationResolution;

            set
            {
                _presentationResolution = value;

                if (RenderMode == RenderMode.Rasterizer)
                {
                    if (RasterizerPipeline != null) RasterizerPipeline.SetSize(RenderResolution.X, RenderResolution.Y, PresentationResolution.X, PresentationResolution.Y);
                }

                if (RenderMode == RenderMode.PathTracer)
                {
                    if (PathTracer != null) PathTracer.SetSize(RenderResolution.X, RenderResolution.Y);
                }

                if (RenderMode == RenderMode.Rasterizer || RenderMode == RenderMode.PathTracer)
                {
                    if (VolumetricLight != null) VolumetricLight.SetSize(PresentationResolution.X, PresentationResolution.Y);
                    if (Bloom != null) Bloom.SetSize(PresentationResolution.X, PresentationResolution.Y);
                    if (TonemapAndGamma != null) TonemapAndGamma.SetSize(PresentationResolution.X, PresentationResolution.Y);
                }
            }
        }

        private RenderMode _renderMode;
        public RenderMode RenderMode
        {
            get => _renderMode;

            set
            {
                // (Re-)Create all rendering ressources for the selected RenderMode.

                _renderMode = value;

                if (RasterizerPipeline != null) { RasterizerPipeline.Dispose(); RasterizerPipeline = null; }
                if (PathTracer != null) { PathTracer.Dispose(); PathTracer = null; }

                if (RenderMode == RenderMode.Rasterizer)
                {
                    RasterizerPipeline = new RasterPipeline(RenderResolution.X, RenderResolution.Y, PresentationResolution.X, PresentationResolution.Y);
                }
                
                if (RenderMode == RenderMode.PathTracer)
                {
                    PathTracer = new PathTracer(RenderResolution.X, RenderResolution.Y);
                }

                if (RenderMode == RenderMode.Rasterizer || RenderMode == RenderMode.PathTracer)
                {
                    if (TonemapAndGamma == null)
                    {
                        TonemapAndGamma = new TonemapAndGammaCorrect(PresentationResolution.X, PresentationResolution.Y, TonemapAndGammaCorrect.GpuSettings.Default);
                    }
                    else
                    {
                        TonemapAndGamma.Dispose();
                        TonemapAndGamma = new TonemapAndGammaCorrect(PresentationResolution.X, PresentationResolution.Y, TonemapAndGamma.Settings);
                    }

                    if (Bloom == null)
                    {
                        Bloom = new Bloom(PresentationResolution.X, PresentationResolution.Y, Bloom.GpuSettings.Default);
                    }
                    else
                    {
                        Bloom.Dispose();
                        Bloom = new Bloom(PresentationResolution.X, PresentationResolution.Y, Bloom.Settings);
                    }

                    if (VolumetricLight == null)
                    {
                        VolumetricLight = new VolumetricLighting(PresentationResolution.X, PresentationResolution.Y, VolumetricLighting.GpuSettings.Default);
                    }
                    else
                    {
                        VolumetricLight.Dispose();
                        VolumetricLight = new VolumetricLighting(PresentationResolution.X, PresentationResolution.Y, VolumetricLight.Settings);
                    }
                }
            }
        }

        public Vector2i RenderResolution => new Vector2i((int)(PresentationResolution.X * ResolutionScale), (int)(PresentationResolution.Y * ResolutionScale));

        public bool RenderGui { get; private set; }
        public int FPS { get; private set; }

        public bool IsBloom = true;
        public bool IsVolumetricLighting = true;
        public bool RunSimulations = true;

        public Intersections.CollisionDetectionSettings SceneVsCamCollisionSettings = new Intersections.CollisionDetectionSettings()
        {
            IsEnabled = true,
            TestSteps = 3,
            RecursiveSteps = 12,
            EpsilonNormalOffset = 0.001f
        };

        private int fpsCounter;
        private readonly Stopwatch fpsTimer = Stopwatch.StartNew();
        protected override void OnRender(float dT)
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

            if (gui.SelectedEntity.EntityType != Gui.EntityType.None)
            {
                Box selectedEntityBox = new Box();
                if (gui.SelectedEntity.EntityType == Gui.EntityType.Mesh)
                {
                    GpuBlasNode node = ModelSystem.BVH.Tlas.Blases[gui.SelectedEntity.EntityID].Root;
                    selectedEntityBox.Min = node.Min;
                    selectedEntityBox.Max = node.Max;

                    selectedEntityBox.Transform(ModelSystem.MeshInstances[gui.SelectedEntity.InstanceID].ModelMatrix);
                }
                else
                {
                    LightManager.TryGetLight(gui.SelectedEntity.EntityID, out CpuLight cpuLight);
                    ref GpuLight light = ref cpuLight.GpuLight;

                    selectedEntityBox.Min = new Vector3(light.Position) - new Vector3(light.Radius);
                    selectedEntityBox.Max = new Vector3(light.Position) + new Vector3(light.Radius);
                }

                BoxRenderer.Render(TonemapAndGamma.Result, GpuBasicData.ProjView, selectedEntityBox);
            }

            Framebuffer.Bind(0);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
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

        private void Update(float dT)
        {
            MainThreadQueue.Execute();

            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FPS = fpsCounter;
                WindowTitle = $"IDKEngine FPS: {FPS}";
                fpsCounter = 0;
                fpsTimer.Restart();
            }

            gui.Update(this);

            //{
            //    int meshID = 71;
            //    int verticesStart = ModelSystem.DrawCommands[meshID].BaseVertex;
            //    int verticesCount = ModelSystem.GetMeshVertexCount(meshID);
            //    for (int i = verticesStart; i < verticesStart + verticesCount; i++)
            //    {
            //        ModelSystem.VertexPositions[i].X += 2f * dT;
            //    }

            //    ModelSystem.UpdateVertexPositions(verticesStart, verticesCount);
            //    ModelSystem.BVH.BlasesRefit(meshID, 1);
            //    //ModelSystem.BVH.TlasBuild();
            //}

            {
                if (KeyboardState[Keys.Escape] == Keyboard.InputState.Pressed)
                {
                    ShouldClose();
                }
                if (KeyboardState[Keys.V] == Keyboard.InputState.Touched)
                {
                    WindowVSync = !WindowVSync;
                }
                if (KeyboardState[Keys.G] == Keyboard.InputState.Touched)
                {
                    RenderGui = !RenderGui;
                    if (!RenderGui)
                    {
                        PresentationResolution = new Vector2i(WindowFramebufferSize.X, WindowFramebufferSize.Y);
                    }
                }
                if (KeyboardState[Keys.F11] == Keyboard.InputState.Touched)
                {
                    WindowFullscreen = !WindowFullscreen;
                }
                if (KeyboardState[Keys.T] == Keyboard.InputState.Touched)
                {
                    RunSimulations = !RunSimulations;
                }
            }

            if (gui.FrameRecState != Gui.FrameRecorderState.Replaying)
            {
                if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
                {
                    if (MouseState[MouseButton.Left] == Keyboard.InputState.Touched)
                    {
                        Vector3 force = Camera.ViewDir * 5.0f;

                        CpuLight newLight = new CpuLight(Camera.Position + Camera.ViewDir * 0.5f, Helper.RandomVec3(32.0f, 88.0f), 0.3f);
                        newLight.Velocity = Camera.Velocity;
                        newLight.AddImpulse(force);

                        Camera.AddImpulse(-force);

                        if (LightManager.AddLight(newLight))
                        {
                            int newLightIndex = LightManager.Count - 1;
                            PointShadow pointShadow = new PointShadow(256, new Vector2(newLight.GpuLight.Radius, 60.0f));
                            LightManager.CreatePointShadowForLight(pointShadow, newLightIndex);
                        }
                    }

                    Camera.ProcessInputs(KeyboardState, MouseState);
                    Camera.AdvanceSimulation(dT);
                }

                if (RunSimulations)
                {
                    LightManager.AdvanceSimulation(dT, ModelSystem);
                }

                if (KeyboardState[Keys.E] == Keyboard.InputState.Touched && !ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
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
            }

            Sphere movingSphere = new Sphere(Camera.PrevPosition, 0.5f);
            Vector3 prevSpherePos = movingSphere.Center;
            Intersections.SceneVsMovingSphereCollisionRoutine(ModelSystem, SceneVsCamCollisionSettings, ref movingSphere, Camera.Position, (in Intersections.SceneHitInfo hitInfo) =>
            {
                Vector3 deltaStep = Camera.Position - prevSpherePos;
                Vector3 slidedDeltaStep = Plane.Project(deltaStep, hitInfo.SlidingPlane);
                Camera.Position = movingSphere.Center + slidedDeltaStep;

                Camera.Velocity = Plane.Project(Camera.Velocity, hitInfo.SlidingPlane); 

                prevSpherePos = movingSphere.Center;
            });

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

            Camera.SetPrevToCurrentPosition();
            LightManager.Update(out bool anyLightMoved);
            ModelSystem.Update(out bool anyMeshInstanceMoved);

            bool cameraMoved = GpuBasicData.PrevProjView != GpuBasicData.ProjView;
            if ((RenderMode == RenderMode.PathTracer) && (cameraMoved || anyMeshInstanceMoved || anyLightMoved))
            {
                PathTracer.ResetRenderProcess();
            }
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
        protected override void OnStart()
        {
            Logger.Log(Logger.LogLevel.Info, $"API: {Helper.API}");
            Logger.Log(Logger.LogLevel.Info, $"GPU: {Helper.GPU}");
            Logger.Log(Logger.LogLevel.Info, $"{nameof(Shader.REPORT_SHADER_ERRORS_WITH_NAME)} = {Shader.REPORT_SHADER_ERRORS_WITH_NAME}");

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
            Helper.SetDepthConvention(Helper.DepthConvention.ZeroToOne);
            GL.Disable(EnableCap.Multisample);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

            basicDataBuffer = new TypedBuffer<GpuBasicData>();
            basicDataBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);
            basicDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 0);

            finalProgram = new ShaderProgram(
                Shader.ShaderFromFile(ShaderType.VertexShader, "ToScreen/vertex.glsl"),
                Shader.ShaderFromFile(ShaderType.FragmentShader, "ToScreen/fragment.glsl")
            );
            Camera = new Camera(RenderResolution, new Vector3(7.63f, 2.71f, 0.8f), -165.4f, 7.4f);
            //Camera = new Camera(RenderResolution, new Vector3(-0.824f, 2.587f, -6.370f), -90.0f, 0.0f);
            //Camera = new Camera(RenderResolution, new Vector3(-13.238f, 4.226f, -0.147f), -360.950f, -14.600f);

            SkyBoxManager.Init(SkyBoxManager.SkyBoxMode.ExternalAsset, new string[]
            {
                "res/textures/environmentMap/posx.jpg",
                "res/textures/environmentMap/negx.jpg",
                "res/textures/environmentMap/posy.jpg",
                "res/textures/environmentMap/negy.jpg",
                "res/textures/environmentMap/posz.jpg",
                "res/textures/environmentMap/negz.jpg"
            });

            PresentationResolution = WindowFramebufferSize;
            BoxRenderer = new BoxRenderer();
            LightManager = new LightManager(12, 12);
            ModelSystem = new ModelSystem();
            
            ModelLoader.SetCallackTextureLoaded(() =>
            {
                if (PathTracer != null)
                {
                    PathTracer.ResetRenderProcess();
                }
            });

            if (true)
            {
                RenderMode = RenderMode.Rasterizer;

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

                //ModelLoader.Model plane = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\Plane.gltf", Matrix4.CreateScale(30.0f, 1.0f, 30.0f) * Matrix4.CreateRotationZ(MathF.PI) * Matrix4.CreateTranslation(0.0f, 17.0f, 10.0f));
                //ModelLoader.Model test = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\glTF-Sample-Models\2.0\SimpleInstancing\glTF\\SimpleInstancing.gltf", Matrix4.CreateRotationY(MathF.PI / 4.0f));
                //ModelLoader.Model bistro = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\BistroExterior\Bistro.gltf");

                ModelSystem.Add(sponza, lucy, helmet);

                LightManager.AddLight(new CpuLight(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(429.8974f, 22.459948f, 28.425867f), 0.3f));
                LightManager.AddLight(new CpuLight(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(8.773416f, 506.7525f, 28.425867f), 0.3f));
                LightManager.AddLight(new CpuLight(new Vector3(4.5f, 5.7f, -2.0f), /*new Vector3(-4.0f, 0.0f, 0.0f), */new Vector3(8.773416f, 22.459948f, 533.77466f), 0.3f));

                //LightManager.AddLight(new CpuLight(new Vector3(-4.5f, 5.7f, -0.5f), Helper.RandomVec3(2.0f, 4.0f), 0.3f));
                //LightManager.AddLight(new CpuLight(new Vector3(-3.9f, 5.7f, -0.5f), Helper.RandomVec3(2.0f, 4.0f), 0.3f));
                //LightManager.AddLight(new CpuLight(new Vector3(-3.3f, 5.7f, -0.5f), Helper.RandomVec3(2.0f, 4.0f), 0.3f));
                //LightManager.AddLight(new CpuLight(new Vector3(-2.7f, 5.7f, -0.5f), Helper.RandomVec3(2.0f, 4.0f), 0.3f));

                for (int i = 0; i < 3; i++)
                {
                    if (LightManager.TryGetLight(i, out CpuLight light))
                    {
                        PointShadow pointShadow = new PointShadow(512, new Vector2(light.GpuLight.Radius, 60.0f));
                        LightManager.CreatePointShadowForLight(pointShadow, i);
                    }
                }

                //LightManager.AddLight(new CpuLight(new Vector3(-12.25f, 7.8f, 0.3f), new Vector3(72.77023f, 36.716278f, 18.192558f) * 0.6f, 1.0f));
                //LightManager.CreatePointShadowForLight(new PointShadow(512, new Vector2(0.5f, 60.0f)), LightManager.Count - 1);
            }
            else
            {
                RenderMode = RenderMode.Rasterizer;

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

                RasterizerPipeline.IsVXGI = false;
                RasterizerPipeline.Voxelizer.GridMin = new Vector3(-18.0f, -1.2f, -11.9f);
                RasterizerPipeline.Voxelizer.GridMax = new Vector3(21.3f, 19.7f, 17.8f);
                RasterizerPipeline.ConeTracer.MaxSamples = 4;

                VolumetricLight.Settings.Strength = 10.0f;
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
                PresentationResolution = new Vector2i(WindowFramebufferSize.X, WindowFramebufferSize.Y);
            }
        }

        protected override void OnKeyPress(char key)
        {
            gui.Backend.PressChar(key);
        }
    }
}
