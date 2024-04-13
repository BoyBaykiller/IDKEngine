using System;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Utils;
using IDKEngine.Render;
using IDKEngine.Shapes;
using IDKEngine.OpenGL;
using IDKEngine.GpuTypes;
using IDKEngine.Windowing;

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

                // trigger ressource re-creation (with new resolution scale)
                PresentationResolution = PresentationResolution;
            }
        }
        public Vector2i RenderResolution => new Vector2i((int)(PresentationResolution.X * ResolutionScale), (int)(PresentationResolution.Y * ResolutionScale));

        private Vector2i _presentationResolution;
        public Vector2i PresentationResolution
        {
            get => _presentationResolution;

            set
            {
                // Having this here enables resizing on AMD drivers without occasional crashing.
                // Normally this shouldnt be necessary and its probably an other driver bug.
                GL.Finish();

                _presentationResolution = value;

                if (RenderMode == RenderMode.Rasterizer)
                {
                    if (RasterizerPipeline != null) RasterizerPipeline.SetSize(RenderResolution, PresentationResolution);
                }

                if (RenderMode == RenderMode.PathTracer)
                {
                    if (PathTracer != null) PathTracer.SetSize(RenderResolution);
                }

                if (RenderMode == RenderMode.Rasterizer || RenderMode == RenderMode.PathTracer)
                {
                    if (VolumetricLight != null) VolumetricLight.SetSize(PresentationResolution);
                    if (Bloom != null) Bloom.SetSize(PresentationResolution);
                    if (TonemapAndGamma != null) TonemapAndGamma.SetSize(PresentationResolution);
                    if (LightManager != null) LightManager.SetSizeRayTracedShadows(RenderResolution);
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

                if (RenderMode == RenderMode.Rasterizer)
                {
                    if (RasterizerPipeline != null) RasterizerPipeline.Dispose();
                    RasterizerPipeline = new RasterPipeline(RenderResolution, PresentationResolution);
                }
                else
                {
                    if (RasterizerPipeline != null) { RasterizerPipeline.Dispose(); RasterizerPipeline = null;  }
                }

                if (RenderMode == RenderMode.PathTracer)
                {
                    if (PathTracer != null) PathTracer.Dispose();
                    PathTracer = new PathTracer(RenderResolution, PathTracer == null ? PathTracer.GpuSettings.Default : PathTracer.GetGpuSettings());
                }
                else
                {
                    if (PathTracer != null) { PathTracer.Dispose(); PathTracer = null; }
                }

                if (RenderMode == RenderMode.Rasterizer || RenderMode == RenderMode.PathTracer)
                {
                    if (BoxRenderer != null) BoxRenderer.Dispose();
                    BoxRenderer = new BoxRenderer();

                    if (TonemapAndGamma != null) TonemapAndGamma.Dispose();
                    TonemapAndGamma = new TonemapAndGammaCorrect(PresentationResolution, TonemapAndGamma == null ? TonemapAndGammaCorrect.GpuSettings.Default : TonemapAndGamma.Settings);

                    if (Bloom != null) Bloom.Dispose();
                    Bloom = new Bloom(PresentationResolution, Bloom == null ? Bloom.GpuSettings.Default : Bloom.Settings);

                    if (VolumetricLight != null) VolumetricLight.Dispose();
                    VolumetricLight = new VolumetricLighting(PresentationResolution, VolumetricLight == null ? VolumetricLighting.GpuSettings.Default : VolumetricLight.Settings);
                }
            }
        }

        public int FPS { get; private set; }

        public bool RenderGui;
        public bool IsBloom = true;
        public bool IsVolumetricLighting = true;
        public bool RunSimulations = true;

        public Intersections.SceneVsMovingSphereSettings SceneVsCamCollisionSettings = new Intersections.SceneVsMovingSphereSettings()
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
                RasterizerPipeline.Render(ModelSystem, LightManager, Camera, dT);
                if (RasterizerPipeline.IsConfigureGridMode)
                {
                    TonemapAndGamma.Combine(RasterizerPipeline.Result);
                    BoxRenderer.Render(TonemapAndGamma.Result, GpuBasicData.ProjView, new Box(RasterizerPipeline.Voxelizer.GridMin, RasterizerPipeline.Voxelizer.GridMax));
                }
                else
                {
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
                if (KeyboardState[Keys.D1] == Keyboard.InputState.Touched)
                {
                    AbstractShaderProgram.RecompileAll();
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
                            PointShadow pointShadow = new PointShadow(256, RenderResolution, new Vector2(newLight.GpuLight.Radius, 60.0f));
                            if (!LightManager.CreatePointShadowForLight(pointShadow, newLightIndex))
                            {
                                pointShadow.Dispose();
                            }
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

                gpuPerFrameBuffer.UploadElements(GpuBasicData);
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
        private AbstractShaderProgram finalProgram;

        public Camera Camera;
        public ModelSystem ModelSystem;
        public StateRecorder<FrameState> FrameStateRecorder;

        public VolumetricLighting VolumetricLight;
        public Bloom Bloom;
        public TonemapAndGammaCorrect TonemapAndGamma;
        public BoxRenderer BoxRenderer;

        public LightManager LightManager;

        public RasterPipeline RasterizerPipeline;
        public PathTracer PathTracer;

        private TypedBuffer<GpuPerFrameData> gpuPerFrameBuffer;
        public GpuPerFrameData GpuBasicData;
        protected override void OnStart()
        {
            Logger.Log(Logger.LogLevel.Info, $"API: {Helper.API}");
            Logger.Log(Logger.LogLevel.Info, $"GPU: {Helper.GPU}");
            Logger.Log(Logger.LogLevel.Info, $"{nameof(AbstractShader.Preprocessor.SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH)} = {AbstractShader.Preprocessor.SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH}");

            if (Helper.APIVersion < 4.6)
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support OpenGL 4.6");
                Environment.Exit(0);
            }
            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support GL_ARB_bindless_texture");
                Environment.Exit(0);
            }
            if (!Helper.IsExtensionsAvailable("GL_EXT_shader_image_load_formatted"))
            {
                Logger.Log(Logger.LogLevel.Fatal, 
                    "Your system does not support GL_EXT_shader_image_load_formatted.\n" +
                    "Execution is still continued because some AMD drivers have a bug to not report this extension even though its there.\n" +
                    "https://community.amd.com/t5/opengl-vulkan/opengl-bug-gl-ext-shader-image-load-formatted-not-reported-even/m-p/676326#M5140\n" +
                    "If the extension is indeed not supported the app may behave incorrectly"
                );
            }

            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageCallback(Helper.GLDebugCallbackFuncPtr, IntPtr.Zero);
            GL.Disable(EnableCap.Multisample);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            Helper.SetDepthConvention(Helper.DepthConvention.ZeroToOne);

            gpuPerFrameBuffer = new TypedBuffer<GpuPerFrameData>();
            gpuPerFrameBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.Dynamic, 1);
            gpuPerFrameBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 0);

            finalProgram = new AbstractShaderProgram(
                new AbstractShader(ShaderType.VertexShader, "ToScreen/vertex.glsl"),
                new AbstractShader(ShaderType.FragmentShader, "ToScreen/fragment.glsl")
            );

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
            ModelSystem = new ModelSystem();

            ModelLoader.SetCallackTextureLoaded(() =>
            {
                if (PathTracer != null)
                {
                    PathTracer.ResetRenderProcess();
                }
            });

            Camera = new Camera(RenderResolution, new Vector3(7.63f, 2.71f, 0.8f), -165.4f, 7.4f);
            if (true)
            {
                ModelLoader.Model sponza = ModelLoader.GltfToEngineFormat("res/models/SponzaCompressed/glTF/Sponza.gltf", Matrix4.CreateScale(1.815f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f));
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

                ModelLoader.Model lucy = ModelLoader.GltfToEngineFormat("res/models/LucyCompressed/Lucy.gltf", Matrix4.CreateScale(0.8f) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(90.0f)) * Matrix4.CreateTranslation(-1.68f, 2.3f, 0.0f));
                lucy.Meshes[0].SpecularBias = -1.0f;
                lucy.Meshes[0].TransmissionBias = 0.98f;
                lucy.Meshes[0].IORBias = 0.174f;
                lucy.Meshes[0].AbsorbanceBias = new Vector3(0.81f, 0.18f, 0.0f);
                lucy.Meshes[0].RoughnessBias = -1.0f;

                ModelLoader.Model helmet = ModelLoader.GltfToEngineFormat("res/models/HelmetCompressed/Helmet.gltf", Matrix4.CreateRotationY(MathF.PI / 4.0f));

                //ModelLoader.Model plane = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\Plane.gltf", Matrix4.CreateScale(30.0f, 1.0f, 30.0f) * Matrix4.CreateRotationZ(MathF.PI) * Matrix4.CreateTranslation(0.0f, 17.0f, 10.0f));
                //ModelLoader.Model test = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\glTF-Sample-Models\2.0\SimpleInstancing\glTF\\SimpleInstancing.gltf", Matrix4.CreateRotationY(MathF.PI / 4.0f));
                //ModelLoader.Model bistro = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\BistroExterior\Bistro.gltf");

                ModelSystem.Add(sponza, lucy, helmet);

                RenderMode = RenderMode.Rasterizer;

                LightManager = new LightManager();
                LightManager.AddLight(new CpuLight(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(429.8974f, 22.459948f, 28.425867f), 0.3f));
                LightManager.AddLight(new CpuLight(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(8.773416f, 506.7525f, 28.425867f), 0.3f));
                LightManager.AddLight(new CpuLight(new Vector3(4.5f, 5.7f, -2.0f), /*new Vector3(-4.0f, 0.0f, 0.0f), */new Vector3(8.773416f, 22.459948f, 533.77466f), 0.3f));

                for (int i = 0; i < 3; i++)
                {
                    if (LightManager.TryGetLight(i, out CpuLight light))
                    {
                        PointShadow pointShadow = new PointShadow(512, RenderResolution, new Vector2(light.GpuLight.Radius, 60.0f));
                        LightManager.CreatePointShadowForLight(pointShadow, i);
                    }
                }

                //LightManager.AddLight(new CpuLight(new Vector3(-12.25f, 7.8f, 0.3f), new Vector3(72.77023f, 36.716278f, 18.192558f) * (0.6f / 0.09f), 0.3f));
                //LightManager.CreatePointShadowForLight(new PointShadow(512, RenderResolution, new Vector2(0.5f, 60.0f)), LightManager.Count - 1);
            }
            else
            {
                ModelLoader.Model a = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\BaseCompressed\NewSponza_Main_glTF_002.gltf");
                //a.MeshInstances[28].ModelMatrix = Matrix4.CreateTranslation(-1000.0f, 0.0f, 0.0f);
                //a.MeshInstances[89].ModelMatrix = Matrix4.CreateTranslation(-1000.0f, 0.0f, 0.0f);
                //a.MeshInstances[271].ModelMatrix = Matrix4.CreateTranslation(-1000.0f, 0.0f, 0.0f);
                //a.Meshes[288].SpecularBias = 1.0f;
                //a.Meshes[288].RoughnessBias = -0.64f;
                //a.Meshes[288].NormalMapStrength = 0.0f;
                //a.Meshes[272].SpecularBias = 1.0f;
                //a.Meshes[272].RoughnessBias = -0.82f;
                //a.Meshes[93].EmissiveBias = 20.0f;
                //a.Meshes[96].EmissiveBias = 20.0f;
                //a.Meshes[99].EmissiveBias = 20.0f;
                //a.Meshes[102].EmissiveBias = 20.0f;
                //a.Meshes[105].EmissiveBias = 20.0f;
                //a.Meshes[108].EmissiveBias = 20.0f;
                //a.Meshes[111].EmissiveBias = 20.0f;
                //a.Meshes[246].EmissiveBias = 20.0f;
                //a.Meshes[291].EmissiveBias = 20.0f;
                //a.Meshes[294].EmissiveBias = 20.0f;
                //a.Meshes[297].EmissiveBias = 20.0f;
                //a.Meshes[300].EmissiveBias = 20.0f;
                //a.Meshes[303].EmissiveBias = 20.0f;
                //a.Meshes[306].EmissiveBias = 20.0f;
                //a.Meshes[312].EmissiveBias = 20.0f;
                //a.Meshes[315].EmissiveBias = 20.0f;
                //a.Meshes[318].EmissiveBias = 20.0f;
                //a.Meshes[321].EmissiveBias = 20.0f;
                //a.Meshes[324].EmissiveBias = 20.0f;
                //a.Meshes[376].EmissiveBias = 20.0f;
                //a.Meshes[379].EmissiveBias = 20.0f;
                ModelLoader.Model b = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\CurtainsCompressed\NewSponza_Curtains_glTF.gltf");
                //ModelLoader.Model c = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Ivy\NewSponza_IvyGrowth_glTF.gltf");
                //ModelLoader.Model d = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Tree\NewSponza_CypressTree_glTF.gltf");
                //ModelLoader.Model e = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Candles\NewSponza_4_Combined_glTF.gltf");
                ModelSystem.Add(a, b);

                LightManager = new LightManager();
                LightManager.AddLight(new CpuLight(new Vector3(-6.256f, 8.415f, -0.315f), new Vector3(820.0f, 560.0f, 586.0f), 0.3f));
                LightManager.CreatePointShadowForLight(new PointShadow(512, RenderResolution, new Vector2(0.1f, 60.0f)), 0);

                RenderMode = RenderMode.Rasterizer;

                RasterizerPipeline.IsVXGI = false;
                RasterizerPipeline.Voxelizer.GridMin = new Vector3(-18.0f, -1.2f, -11.9f);
                RasterizerPipeline.Voxelizer.GridMax = new Vector3(21.3f, 19.7f, 17.8f);
                RasterizerPipeline.ConeTracer.Settings.MaxSamples = 4;
            }

            RenderGui = true;
            WindowVSync = true;
            MouseState.CursorMode = CursorModeValue.CursorNormal;
            FrameStateRecorder = new StateRecorder<FrameState>();
            gui = new Gui(WindowFramebufferSize.X, WindowFramebufferSize.Y);

            GC.Collect();
        }

        protected override void OnEnd()
        {

        }

        protected override void OnWindowResize()
        {
            gui.Backend.SetSize(WindowFramebufferSize);

            if (!RenderGui)
            {
                PresentationResolution = WindowFramebufferSize;
            }
        }

        protected override void OnKeyPress(char key)
        {
            gui.Backend.PressChar(key);
        }
    }
}
