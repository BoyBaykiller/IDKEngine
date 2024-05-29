using System;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Render;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;
using IDKEngine.Windowing;

namespace IDKEngine
{
    class Application : GameWindowBase
    {
        public enum RenderMode : int
        {
            Rasterizer,
            PathTracer
        }

        public RenderMode RenderPath
        {
            get
            {
                if (RasterizerPipeline != null && PathTracer != null)
                {
                    throw new InvalidOperationException($"Invalid state. Rasterizer and PathTracer should never be enabled both");
                }
                if (RasterizerPipeline != null)
                {
                    return RenderMode.Rasterizer;
                }
                if (PathTracer != null)
                {
                    return RenderMode.PathTracer;
                }

                throw new UnreachableException();
            }
        }

        public Vector2i PresentationResolution => new Vector2i(TonemapAndGamma.Result.Width, TonemapAndGamma.Result.Height);
        
        public Vector2i RenderResolution
        {
            get
            {
                if (RenderPath == RenderMode.Rasterizer)
                {
                    return RasterizerPipeline.RenderResolution;
                }

                if (RenderPath == RenderMode.PathTracer)
                {
                    return PathTracer.RenderResolution;
                }

                throw new UnreachableException($"Unknown {nameof(RenderPath)} = {RenderPath}");
            }
        }

        public float RenderResolutionScale => (float)RenderResolution.Y / PresentationResolution.Y;
        

        // These will take effect at the beginning of a frame
        public Vector2i? RequestPresentationResolution;
        public float? RequestRenderResolutionScale;
        public RenderMode? RequestRenderMode;

        // Used in Rasterizer and PathTracer RenderMode respectively
        public RasterPipeline RasterizerPipeline;
        public PathTracer PathTracer;

        // These effects run at presentation resolution and are useful for
        // both Rasterizer and PathTracer RenderMode which is why they are here
        public TonemapAndGammaCorrect TonemapAndGamma;
        public BoxRenderer BoxRenderer;
        public Bloom Bloom;
        public bool IsBloom = true;
        private Gui gui;
        public bool RenderGui = true;
        public VolumetricLighting VolumetricLight;
        public bool IsVolumetricLighting = true;

        // All models and all lights and Camera (the types of different entities)
        public ModelManager ModelManager;
        public LightManager LightManager;
        public Camera Camera;

        public StateRecorder<FrameState> FrameStateRecorder;

        private GpuPerFrameData gpuPerFrameData;
        private BBG.TypedBuffer<GpuPerFrameData> gpuPerFrameDataBuffer;

        public int FramesPerSecond { get; private set; }

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
        private float lastRenderTime = 1.0f / 144.0f;
        public Application(int width, int height, string title)
            : base(width, height, title, 4, 6)
        {
        }

        protected override void GameLoop()
        {
            Stopwatch sw = Stopwatch.StartNew();
            Render(lastRenderTime);
            lastRenderTime = (float)sw.Elapsed.TotalSeconds;
        }

        private void Render(float dT)
        {
            Update(dT);

            if (RequestPresentationResolution.HasValue || RequestRenderResolutionScale.HasValue)
            {
                float newResolutionScale = RequestRenderResolutionScale ?? RenderResolutionScale;
                Vector2i newPresenRes = RequestPresentationResolution ?? PresentationResolution;
                Vector2i newRenderRes = new Vector2i((int)(newPresenRes.X * newResolutionScale), (int)(newPresenRes.Y * newResolutionScale));
                RequestPresentationResolution = null;
                RequestRenderResolutionScale = null;

                SetResolutions(newRenderRes, newPresenRes);
            }

            if (RequestRenderMode.HasValue)
            {
                RenderMode newRenderMode = RequestRenderMode.Value;
                RequestRenderMode = null;

                SetRenderMode(newRenderMode, RenderResolution, PresentationResolution);
            }

            if (RenderPath == RenderMode.Rasterizer)
            {
                RasterizerPipeline.Render(ModelManager, LightManager, Camera, dT);
                if (RasterizerPipeline.IsConfigureGridMode)
                {
                    TonemapAndGamma.Compute(RasterizerPipeline.Result);
                    BoxRenderer.Render(TonemapAndGamma.Result, gpuPerFrameData.ProjView, new Box(RasterizerPipeline.Voxelizer.GridMin, RasterizerPipeline.Voxelizer.GridMax));
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

                    TonemapAndGamma.Compute(RasterizerPipeline.Result, IsBloom ? Bloom.Result : null, IsVolumetricLighting ? VolumetricLight.Result : null);
                    RasterizerPipeline.LightingVRS.DebugRender(TonemapAndGamma.Result);
                }
            }
            
            if (RenderPath == RenderMode.PathTracer)
            {
                PathTracer.Compute();

                if (IsBloom)
                {
                    Bloom.Compute(PathTracer.Result);
                }

                TonemapAndGamma.Compute(PathTracer.Result, IsBloom ? Bloom.Result : null);
            }

            if (gui.SelectedEntity.EntityType != Gui.EntityType.None)
            {
                Box boundingBox = new Box();
                if (gui.SelectedEntity.EntityType == Gui.EntityType.Mesh)
                {
                    GpuBlasNode node = ModelManager.BVH.Tlas.Blases[gui.SelectedEntity.EntityID].Root;
                    boundingBox.Min = node.Min;
                    boundingBox.Max = node.Max;

                    boundingBox.Transform(ModelManager.MeshInstances[gui.SelectedEntity.InstanceID].ModelMatrix);
                }

                if (gui.SelectedEntity.EntityType == Gui.EntityType.Light)
                {
                    LightManager.TryGetLight(gui.SelectedEntity.EntityID, out CpuLight cpuLight);
                    ref GpuLight light = ref cpuLight.GpuLight;

                    boundingBox.Min = light.Position - new Vector3(light.Radius);
                    boundingBox.Max = light.Position + new Vector3(light.Radius);
                }

                BoxRenderer.Render(TonemapAndGamma.Result, gpuPerFrameData.ProjView, boundingBox);
            }

            BBG.Rendering.SetViewport(WindowFramebufferSize);
            if (RenderGui)
            {
                gui.Draw(this, dT);
            }
            else
            {
                BBG.Rendering.CopyTextureToSwapchain(TonemapAndGamma.Result);
            }

            PollEvents();
            SwapBuffers();

            fpsCounter++;
        }

        private void Update(float dT)
        {
            MainThreadQueue.Execute();

            gui.Update(this);

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
                        RequestPresentationResolution = WindowFramebufferSize;
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
                    BBG.AbstractShaderProgram.RecompileAll();
                }
            }

            if (gui.RecordingVars.FrameRecState != Gui.FrameRecorderState.Replaying)
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
                            CpuPointShadow pointShadow = new CpuPointShadow(256, RenderResolution, new Vector2(newLight.GpuLight.Radius, 60.0f));
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
                    LightManager.AdvanceSimulation(dT, ModelManager);
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
            Intersections.SceneVsMovingSphereCollisionRoutine(ModelManager, SceneVsCamCollisionSettings, ref movingSphere, Camera.Position, (in Intersections.SceneHitInfo hitInfo) =>
            {
                Vector3 deltaStep = Camera.Position - prevSpherePos;
                Vector3 slidedDeltaStep = Plane.Project(deltaStep, hitInfo.SlidingPlane);
                Camera.Position = movingSphere.Center + slidedDeltaStep;

                Camera.Velocity = Plane.Project(Camera.Velocity, hitInfo.SlidingPlane); 

                prevSpherePos = movingSphere.Center;
            });

            {
                Camera.ProjectionSize = RenderResolution;

                gpuPerFrameData.PrevView = gpuPerFrameData.View;
                gpuPerFrameData.PrevProjView = gpuPerFrameData.ProjView;

                gpuPerFrameData.Projection = Camera.GetProjectionMatrix();
                gpuPerFrameData.InvProjection = gpuPerFrameData.Projection.Inverted();

                gpuPerFrameData.View = Camera.GetViewMatrix();
                gpuPerFrameData.InvView = gpuPerFrameData.View.Inverted();

                gpuPerFrameData.ProjView = gpuPerFrameData.View * gpuPerFrameData.Projection;
                gpuPerFrameData.InvProjView = gpuPerFrameData.ProjView.Inverted();

                gpuPerFrameData.CameraPos = Camera.Position;
                gpuPerFrameData.NearPlane = Camera.NearPlane;
                gpuPerFrameData.FarPlane = Camera.FarPlane;

                gpuPerFrameData.DeltaRenderTime = dT;
                gpuPerFrameData.Time = WindowTime;
                gpuPerFrameData.Frame++;

                gpuPerFrameDataBuffer.UploadElements(gpuPerFrameData);
            }

            Camera.SetPrevToCurrentPosition();
            LightManager.Update(out bool anyLightMoved);
            ModelManager.Update(out bool anyMeshInstanceMoved);
            ModelManager.BVH.TlasBuild();

            bool cameraMoved = gpuPerFrameData.PrevProjView != gpuPerFrameData.ProjView;
            if ((RenderPath == RenderMode.PathTracer) && (cameraMoved || anyMeshInstanceMoved || anyLightMoved))
            {
                PathTracer.ResetRenderProcess();
            }
            
            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FramesPerSecond = fpsCounter;
                WindowTitle = $"IDKEngine FPS: {FramesPerSecond}";
                fpsCounter = 0;
                fpsTimer.Restart();
            }
        }

        protected unsafe override void OnStart()
        {
            BBG.Initialize(Helper.GLDebugCallback);

            ref readonly BBG.ContextInfo glContextInfo = ref BBG.GetContextInfo();
            
            Logger.Log(Logger.LogLevel.Info, $"API: {glContextInfo.Name}");
            Logger.Log(Logger.LogLevel.Info, $"GPU: {glContextInfo.DeviceInfo.Name}");
            Logger.Log(Logger.LogLevel.Info, $"{nameof(BBG.AbstractShader.Preprocessor.SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH)} = {BBG.AbstractShader.Preprocessor.SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH}");

            if (glContextInfo.GLVersion < 4.6)
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support OpenGL 4.6");
                Environment.Exit(0);
            }
            if (!glContextInfo.DeviceInfo.ExtensionSupport.BindlessTextures)
            {
                Logger.Log(Logger.LogLevel.Fatal, "Your system does not support GL_ARB_bindless_texture");
                Environment.Exit(0);
            }
            if (!glContextInfo.DeviceInfo.ExtensionSupport.ImageLoadFormatted)
            {
                Logger.Log(Logger.LogLevel.Fatal, 
                    "Your system does not support GL_EXT_shader_image_load_formatted.\n" +
                    "Execution is still continued because some AMD drivers have a bug to not report this extension even though its there.\n" +
                    "https://community.amd.com/t5/opengl-vulkan/opengl-bug-gl-ext-shader-image-load-formatted-not-reported-even/m-p/676326#M5140\n" +
                    "If the extension is indeed not supported shader compilation will throw errors"
                );
            }

            gpuPerFrameDataBuffer = new BBG.TypedBuffer<GpuPerFrameData>();
            gpuPerFrameDataBuffer.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, 1);
            gpuPerFrameDataBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 0);
            
            SkyBoxManager.Initialize(SkyBoxManager.SkyBoxMode.ExternalAsset, [
                "Ressource/Textures/EnvironmentMap/1.jpg",
                "Ressource/Textures/EnvironmentMap/2.jpg",
                "Ressource/Textures/EnvironmentMap/3.jpg",
                "Ressource/Textures/EnvironmentMap/4.jpg",
                "Ressource/Textures/EnvironmentMap/5.jpg",
                "Ressource/Textures/EnvironmentMap/6.jpg"
            ]);

            ModelLoader.TextureLoaded += () =>
            {
                if (PathTracer != null)
                {
                    PathTracer.ResetRenderProcess();
                }
            };

            ModelManager = new ModelManager();
            LightManager = new LightManager();

            Camera = new Camera(WindowFramebufferSize, new Vector3(7.63f, 2.71f, 0.8f), -165.4f, 7.4f);
            if (true)
            {
                ModelLoader.Model sponza = ModelLoader.LoadGltfFromFile("Ressource/Models/SponzaCompressed/Sponza.gltf", Matrix4.CreateScale(1.815f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f));
                sponza.Meshes[63].EmissiveBias = 10.0f;
                sponza.Meshes[70].EmissiveBias = 20.0f;
                sponza.Meshes[3].EmissiveBias = 12.0f;
                sponza.Meshes[99].EmissiveBias = 15.0f;
                sponza.Meshes[97].EmissiveBias = 9.0f;
                sponza.Meshes[42].EmissiveBias = 20.0f;
                sponza.Meshes[38].EmissiveBias = 20.0f;
                sponza.Meshes[40].EmissiveBias = 20.0f;
                sponza.Meshes[42].EmissiveBias = 20.0f;
                //sponza.Meshes[46].SpecularBias = 1.0f;
                //sponza.Meshes[46].RoughnessBias = -0.436f;

                ModelLoader.Model lucy = ModelLoader.LoadGltfFromFile("Ressource/Models/LucyCompressed/Lucy.gltf", Matrix4.CreateScale(0.8f) * Matrix4.CreateRotationY(MathHelper.DegreesToRadians(90.0f)) * Matrix4.CreateTranslation(-1.68f, 2.3f, 0.0f));
                lucy.Meshes[0].SpecularBias = -1.0f;
                lucy.Meshes[0].TransmissionBias = 0.98f;
                lucy.Meshes[0].IORBias = 0.174f;
                lucy.Meshes[0].AbsorbanceBias = new Vector3(0.81f, 0.18f, 0.0f);
                lucy.Meshes[0].RoughnessBias = -1.0f;

                ModelLoader.Model helmet = ModelLoader.LoadGltfFromFile("Ressource/Models/HelmetCompressed/Helmet.gltf", Matrix4.CreateRotationY(MathF.PI / 4.0f));

                //ModelLoader.Model plane = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\Plane.gltf", Matrix4.CreateScale(30.0f, 1.0f, 30.0f) * Matrix4.CreateRotationZ(MathF.PI) * Matrix4.CreateTranslation(0.0f, 17.0f, 10.0f));
                //ModelLoader.Model test = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\glTF-Sample-Models\2.0\SimpleInstancing\glTF\\SimpleInstancing.gltf", Matrix4.CreateRotationY(MathF.PI / 4.0f));
                //ModelLoader.Model bistro = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\Bistro\BistroExteriorCompressed\BistroExteriorCompressed.gltf");

                ModelManager.Add(sponza, lucy, helmet);

                SetRenderMode(RenderMode.Rasterizer, WindowFramebufferSize, WindowFramebufferSize);

                LightManager.AddLight(new CpuLight(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(429.8974f, 22.459948f, 28.425867f), 0.3f));
                LightManager.AddLight(new CpuLight(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(8.773416f, 506.7525f, 28.425867f), 0.3f));
                LightManager.AddLight(new CpuLight(new Vector3(4.5f, 5.7f, -2.0f), /*new Vector3(-4.0f, 0.0f, 0.0f), */new Vector3(8.773416f, 22.459948f, 533.77466f), 0.3f));

                for (int i = 0; i < 3; i++)
                {
                    if (LightManager.TryGetLight(i, out CpuLight light))
                    {
                        CpuPointShadow pointShadow = new CpuPointShadow(512, WindowFramebufferSize, new Vector2(light.GpuLight.Radius, 60.0f));
                        LightManager.CreatePointShadowForLight(pointShadow, i);
                    }
                }

                //LightManager.AddLight(new CpuLight(new Vector3(-12.25f, 7.8f, 0.3f), new Vector3(72.77023f, 36.716278f, 18.192558f) * (0.6f / 0.09f), 0.3f));
                //LightManager.CreatePointShadowForLight(new PointShadow(512, WindowFramebufferSize, new Vector2(0.5f, 60.0f)), LightManager.Count - 1);
            }
            else
            {
                ModelLoader.Model a = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Base\Compressed\NewSponza_Main_glTF_002.gltf");
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
                ModelLoader.Model b = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Curtains\Compressed\NewSponza_Curtains_glTF.gltf");
                //ModelLoader.Model c = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Ivy\Compressed\NewSponza_IvyGrowth_glTF.gltf");
                //ModelLoader.Model d = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Tree\Compressed\NewSponza_CypressTree_glTF.gltf");
                //ModelLoader.Model e = ModelLoader.GltfToEngineFormat(@"C:\Users\Julian\Downloads\Models\IntelSponza\Candles\NewSponza_4_Combined_glTF.gltf");
                ModelManager.Add(a, b);

                SetRenderMode(RenderMode.Rasterizer, WindowFramebufferSize, WindowFramebufferSize);

                RasterizerPipeline.IsVXGI = false;
                RasterizerPipeline.Voxelizer.GridMin = new Vector3(-18.0f, -1.2f, -11.9f);
                RasterizerPipeline.Voxelizer.GridMax = new Vector3(21.3f, 19.7f, 17.8f);
                RasterizerPipeline.ConeTracer.Settings.MaxSamples = 4;

                LightManager.AddLight(new CpuLight(new Vector3(-6.256f, 8.415f, -0.315f), new Vector3(820.0f, 560.0f, 586.0f), 0.3f));
                LightManager.CreatePointShadowForLight(new CpuPointShadow(512, WindowFramebufferSize, new Vector2(0.1f, 60.0f)), 0);
            }

            gui = new Gui(WindowFramebufferSize.X, WindowFramebufferSize.Y);
            WindowVSync = true;
            MouseState.CursorMode = CursorModeValue.CursorNormal;
            FrameStateRecorder = new StateRecorder<FrameState>();

            GC.Collect();
        }

        protected override void OnWindowResize()
        {
            gui.SetSize(WindowFramebufferSize);

            // If GUI is used it will handle resizing
            if (!RenderGui)
            {
                RequestPresentationResolution = WindowFramebufferSize;
            }
        }

        protected override void OnKeyPress(char key)
        {
            gui.PressChar(key);
        }

        /// <summary>
        /// We should avoid random resolution changes inside a frame so if you can use
        /// <seealso cref="RequestPresentationResolution"/> instead.
        /// It will always make the change at the beginning of a frame.
        /// </summary>
        private void SetResolutions(Vector2i renderRes, Vector2i presentRes)
        {
            if (RenderPath == RenderMode.Rasterizer)
            {
                if (RasterizerPipeline != null) RasterizerPipeline.SetSize(renderRes, presentRes);
            }
            if (RenderPath == RenderMode.PathTracer)
            {
                if (PathTracer != null) PathTracer.SetSize(renderRes);
            }
            if (RenderPath == RenderMode.Rasterizer || RenderPath == RenderMode.PathTracer)
            {
                if (VolumetricLight != null) VolumetricLight.SetSize(presentRes);
                if (Bloom != null) Bloom.SetSize(presentRes);
                if (TonemapAndGamma != null) TonemapAndGamma.SetSize(presentRes);
                if (LightManager != null) LightManager.SetSizeRayTracedShadows(renderRes);
            }
        }

        /// <summary>
        /// We should avoid random render mode changes inside a frame so if you can use
        /// <seealso cref="RequestRenderMode"/> instead.
        /// It will always make the change at the beginning of a frame.
        /// </summary>
        private void SetRenderMode(RenderMode renderMode, Vector2i renderRes, Vector2i presentRes)
        {
            if (renderMode == RenderMode.Rasterizer)
            {
                if (RasterizerPipeline != null) RasterizerPipeline.Dispose();
                RasterizerPipeline = new RasterPipeline(renderRes, presentRes);
            }
            else
            {
                if (RasterizerPipeline != null) { RasterizerPipeline.Dispose(); RasterizerPipeline = null; }
            }

            if (renderMode == RenderMode.PathTracer)
            {
                if (PathTracer != null) PathTracer.Dispose();
                PathTracer = new PathTracer(renderRes, PathTracer == null ? PathTracer.GpuSettings.Default : PathTracer.GetGpuSettings());
            }
            else
            {
                if (PathTracer != null) { PathTracer.Dispose(); PathTracer = null; }
            }

            if (renderMode == RenderMode.Rasterizer || renderMode == RenderMode.PathTracer)
            {
                if (BoxRenderer != null) BoxRenderer.Dispose();
                BoxRenderer = new BoxRenderer();

                if (TonemapAndGamma != null) TonemapAndGamma.Dispose();
                TonemapAndGamma = new TonemapAndGammaCorrect(presentRes, TonemapAndGamma == null ? TonemapAndGammaCorrect.GpuSettings.Default : TonemapAndGamma.Settings);

                if (Bloom != null) Bloom.Dispose();
                Bloom = new Bloom(presentRes, Bloom == null ? Bloom.GpuSettings.Default : Bloom.Settings);

                if (VolumetricLight != null) VolumetricLight.Dispose();
                VolumetricLight = new VolumetricLighting(presentRes, VolumetricLight == null ? VolumetricLighting.GpuSettings.Default : VolumetricLight.Settings);
            }
        }

        public ref readonly GpuPerFrameData GetPerFrameData()
        {
            return ref gpuPerFrameData;
        }
    }
}
