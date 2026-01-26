using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.Render;
using IDKEngine.GpuTypes;
using IDKEngine.Windowing;

namespace IDKEngine;

class Application : GameWindowBase
{
    public enum RenderMode : int
    {
        Rasterizer,
        PathTracer
    }

    public enum FrameRecorderState : int
    {
        None,
        Recording,
        Replaying,
    }

    public record struct RecordingSettings
    {
        // To merge recorded frames into video:
        // ffmpeg.exe -framerate 144 -thread_queue_size 4096 -start_number 1 -i '%d.jpg' -vcodec libx264 -crf 22 -pix_fmt yuv420p -vf "scale=trunc(iw/2)*2:trunc(ih/2)*2" "../render.mp4"

        public const string FRAME_RECORD_FILE_EXTENSION = ".frd";
        public const string FRAMES_OUTPUT_FOLDER = "RecordedFrames";

        public int FPSGoal = 30;
        public int PathTracerSamples = 50;
        public bool DoDenoising = false;
        public bool IsOutputFrames = false;
        public FrameRecorderState State = FrameRecorderState.None;
        public Stopwatch FrameTimer = new Stopwatch();

        public RecordingSettings()
        {
        }
    }

    public RenderMode RenderMode_
    {
        get
        {
            if (RasterizerPipeline != null)
            {
                return RenderMode.Rasterizer;
            }
            if (PathTracerPipeline != null)
            {
                return RenderMode.PathTracer;
            }

            throw new UnreachableException($"Rasterizer and PathTracer are both disposed. Select a {nameof(RenderMode)}");
        }
    }

    public Vector2i PresentationResolution => new Vector2i(TonemapAndGamma.Result.Width, TonemapAndGamma.Result.Height);

    public Vector2i RenderResolution
    {
        get
        {
            if (RenderMode_ == RenderMode.Rasterizer)
            {
                return RasterizerPipeline.RenderResolution;
            }

            if (RenderMode_ == RenderMode.PathTracer)
            {
                return PathTracerPipeline.RenderResolution;
            }

            throw new UnreachableException($"Unknown {nameof(RenderMode_)} = {RenderMode_}");
        }
    }

    public float RenderResolutionScale => (float)RenderResolution.Y / PresentationResolution.Y;

    public ref readonly GpuPerFrameData PerFrameData => ref gpuPerFrameData;

    // Will take effect at the beginning of a frame
    public Vector2i? RequestPresentationResolution;
    public float? RequestRenderResolutionScale;
    public RenderMode? RequestRenderMode;

    // Used for Rasterizer and PathTracer RenderMode
    // Only one is ever used while the other is disposed and set to null
    public RasterPipeline? RasterizerPipeline;
    public PathTracerPipeline? PathTracerPipeline;

    // Run at presentation resolution and are useful for
    // both Rasterizer and PathTracer RenderMode which is why they are here
    public TonemapAndGammaCorrect TonemapAndGamma;
    public BoxRenderer BoxRenderer;
    public Bloom Bloom;
    public VolumetricLighting VolumetricLight;
    private Gui gui;
    public bool IsBloom = true;
    public bool IsVolumetricLighting = true;
    public bool RenderImGui = true;

    // All models and all lights and Camera (the types of different entities)
    public ModelManager ModelManager;
    public LightManager LightManager;
    public Camera Camera;

    public StateRecorder<FrameState> FrameStateRecorder;
    public RecordingSettings RecorderVars = new RecordingSettings();

    public int MeasuredFramesPerSecond { get; private set; }

    public bool TimeEnabled;

    private GpuPerFrameData gpuPerFrameData;
    private BBG.TypedBuffer<GpuPerFrameData> gpuPerFrameDataBuffer;

    private int fpsCounter;
    private readonly Stopwatch fpsTimer = Stopwatch.StartNew();

    private float animationTime;

    public Application(int width, int height, string title)
        : base(width, height, title, 4, 6)
    {
    }

    protected override void OnRender(float dT)
    {
        MainThreadQueue.Execute();

        HandleFrameRecorderLogic();

        Camera.ProjectionSize = RenderResolution;
        gpuPerFrameData.PrevView = gpuPerFrameData.View;
        gpuPerFrameData.PrevProjView = gpuPerFrameData.ProjView;
        gpuPerFrameData.Projection = Camera.GetProjectionMatrix();
        gpuPerFrameData.InvProjection = Matrix4.Invert(gpuPerFrameData.Projection);
        gpuPerFrameData.View = Camera.GetViewMatrix();
        gpuPerFrameData.InvView = Matrix4.Invert(gpuPerFrameData.View);
        gpuPerFrameData.ProjView = gpuPerFrameData.View * gpuPerFrameData.Projection;
        gpuPerFrameData.InvProjView = Matrix4.Invert(gpuPerFrameData.ProjView);
        gpuPerFrameData.CameraPos = Camera.Position;
        gpuPerFrameData.NearPlane = Camera.NearPlane;
        gpuPerFrameData.FarPlane = Camera.FarPlane;
        gpuPerFrameData.DeltaRenderTime = dT;
        gpuPerFrameData.Time = WindowTime;
        gpuPerFrameData.Frame++;
        gpuPerFrameDataBuffer.UploadElements(gpuPerFrameData);

        LightManager.Update(out bool anyLightMoved);
        ModelManager.Update(animationTime, out bool anyAnimatedNodeMoved, out bool anyMeshInstanceMoved);
        //ModelManager.BVH.BlasesBuild(0, ModelManager.BVH.BlasesDesc.Length);

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
            SetRenderMode(RequestRenderMode.Value, RenderResolution, PresentationResolution);
            RequestRenderMode = null;
        }

        if (RenderMode_ == RenderMode.Rasterizer)
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

        if (RenderMode_ == RenderMode.PathTracer)
        {
            bool cameraMoved = gpuPerFrameData.PrevProjView != gpuPerFrameData.ProjView;
            if (cameraMoved || anyAnimatedNodeMoved || anyMeshInstanceMoved || anyLightMoved)
            {
                PathTracerPipeline.ResetAccumulation();
            }

            PathTracerPipeline.Compute();

            if (IsBloom)
            {
                Bloom.Compute(PathTracerPipeline.Result);
            }

            TonemapAndGamma.Settings.DoTonemapAndSrgbTransform = !PathTracerPipeline.DoDebugBVHTraversal;
            TonemapAndGamma.Compute(PathTracerPipeline.Result, IsBloom ? Bloom.Result : null);
        }

        if (gui.SelectedEntity is Gui.SelectedEntityInfo.Mesh meshInfo)
        {
            ref readonly GpuMesh mesh = ref ModelManager.Meshes[meshInfo.MeshId];
            ref readonly GpuMeshTransform meshTransform = ref ModelManager.MeshTransforms[meshInfo.MeshTransformId];

            Box box = new Box(mesh.LocalBoundsMin, mesh.LocalBoundsMax);
            BoxRenderer.Render(TonemapAndGamma.Result, meshTransform.ModelMatrix * gpuPerFrameData.ProjView, box);
        }
        else if (gui.SelectedEntity is Gui.SelectedEntityInfo.Light lightInfo)
        {
            LightManager.TryGetLight(lightInfo.LightId, out CpuLight cpuLight);
            ref GpuLight light = ref cpuLight.GpuLight;

            Box box = new Box(light.Position - new Vector3(light.Radius), light.Position + new Vector3(light.Radius));
            BoxRenderer.Render(TonemapAndGamma.Result, gpuPerFrameData.ProjView, box);
        }
        else if (gui.SelectedEntity is Gui.SelectedEntityInfo.Node nodeInfo)
        {
            ModelLoader.Node.Traverse(nodeInfo.Node_, (node) =>
            {
                if (node.HasMeshes)
                {
                    Range meshInstanceRange = ModelManager.GetMeshesInstanceRange(node.MeshRange);
                    for (int i = meshInstanceRange.Start; i < meshInstanceRange.End; i++)
                    {
                        ref readonly GpuMeshInstance meshInstance = ref ModelManager.MeshInstances[i];

                        ref readonly GpuMesh mesh = ref ModelManager.Meshes[meshInstance.MeshId];
                        ref readonly GpuMeshTransform meshTransform = ref ModelManager.MeshTransforms[meshInstance.MeshTransformId];

                        Box box = new Box(mesh.LocalBoundsMin, mesh.LocalBoundsMax);
                        BoxRenderer.Render(TonemapAndGamma.Result, meshTransform.ModelMatrix * gpuPerFrameData.ProjView, box);
                    }
                }
            });
        }

        BBG.Rendering.SetViewport(WindowFramebufferSize);
        if (RenderImGui)
        {
            gui.Draw(this, dT);
        }
        else
        {
            BBG.Rendering.CopyTextureToSwapchain(TonemapAndGamma.Result);
        }

        SwapBuffers();
        PollEvents();

        fpsCounter++;
        if (fpsTimer.ElapsedMilliseconds >= 1000)
        {
            MeasuredFramesPerSecond = fpsCounter;
            WindowTitle = $"IDKEngine FPS: {MeasuredFramesPerSecond} ({RenderResolution.X}x{RenderResolution.Y})";
            fpsCounter = 0;
            fpsTimer.Restart();

            if (!Debugger.IsAttached)
            {
                // When loading a model like IntelSponza, SharpGLTF and possibly other code
                // creates a lot of garbage and the GC doesn't return it to the OS even after waiting.
                // I don't like that so let's force Collection every second when running standalone
                GC.Collect();
            }
        }
    }

    protected override void OnUpdate(float dT)
    {
        gui.Update(this);

        if (KeyboardState[Keys.Escape] == Keyboard.InputState.Pressed)
        {
            ShouldClose();
        }

        if (!RenderImGui || !ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
        {
            if (KeyboardState[Keys.V] == Keyboard.InputState.Touched)
            {
                WindowVSync = !WindowVSync;
            }
            if (KeyboardState[Keys.G] == Keyboard.InputState.Touched)
            {
                RenderImGui = !RenderImGui;
                if (!RenderImGui)
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
                TimeEnabled = !TimeEnabled;
            }
            if (KeyboardState[Keys.D1] == Keyboard.InputState.Touched)
            {
                BBG.AbstractShaderProgram.RecompileAll();
                PathTracerPipeline?.ResetAccumulation();
            }
            if (KeyboardState[Keys.E] == Keyboard.InputState.Touched)
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

        if (RecorderVars.State != FrameRecorderState.Replaying)
        {
            if (!RenderImGui || !ImGuiNET.ImGui.GetIO().WantCaptureMouse)
            {
                if (MouseState.CursorMode == CursorModeValue.CursorDisabled && MouseState[MouseButton.Left] == Keyboard.InputState.Touched)
                {
                    Vector3 force = Camera.ViewDir * 5.0f;

                    CpuLight newLight = new CpuLight(Camera.Position + Camera.ViewDir * 0.5f, RNG.RandomVec3(32.0f, 88.0f), 0.3f);
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
            }

            if (TimeEnabled)
            {
                animationTime += dT;
            }

            if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
            {
                Camera.ProcessInputs(KeyboardState, MouseState);
                Camera.AdvanceSimulation(dT);

                if (MouseState[MouseButton.Button5] != Keyboard.InputState.Pressed)
                {
                    Camera.CollisionDetection(ModelManager);
                }
            }

            if (TimeEnabled)
            {
                LightManager.AdvanceSimulation(dT);
            }

            LightManager.CollisionDetection(ModelManager);
        }

        Camera.SetPrevToCurrentPosition();
    }

    protected override void OnStart()
    {
        BBG.Initialize(Helper.GLDebugCallback);

        ref readonly BBG.ContextInfo glContextInfo = ref BBG.GetContextInfo();

        Logger.Log(Logger.LogLevel.Info, $"API: {glContextInfo.APIName}");
        Logger.Log(Logger.LogLevel.Info, $"GPU: {glContextInfo.DeviceInfo.Name}");
        Logger.Log(Logger.LogLevel.Info, $"{nameof(BBG.AbstractShader.Preprocessor.SUPPORTS_LINE_DIRECTIVE_SOURCEFILE)} = {BBG.AbstractShader.Preprocessor.SUPPORTS_LINE_DIRECTIVE_SOURCEFILE}");

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
                "Execution is still continued because AMD drivers older than 24.10 have a bug to not report this extension even though its there.\n" +
                "https://community.amd.com/t5/opengl-vulkan/opengl-bug-gl-ext-shader-image-load-formatted-not-reported-even/m-p/676326#M5140\n" +
                "If the extension is indeed not supported shader compilation will throw errors"
            );
        }

        gpuPerFrameDataBuffer = new BBG.TypedBuffer<GpuPerFrameData>();
        gpuPerFrameDataBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, 1);
        gpuPerFrameDataBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.Uniform, 1);

        SkyBoxManager.Initialize();
        SkyBoxManager.SkyBoxImagePaths = ["Resource/Textures/EnvironmentMap/snow_field_puresky_1k.hdr"];
        SkyBoxManager.SetSkyBoxMode(SkyBoxManager.SkyBoxMode.ExternalAsset);

        ModelLoader.TextureLoaded += () =>
        {
            PathTracerPipeline?.ResetAccumulation();
        };

        ModelManager = new ModelManager();
        LightManager = new LightManager();

        gui = new Gui(WindowFramebufferSize);
        Camera = new Camera(WindowFramebufferSize, new Vector3(7.63f, 2.71f, 0.8f), 360.0f - 165.4f, 90.0f - 7.4f);

        if (true)
        {
            ModelLoader.Model sponza = ModelLoader.LoadGltfFromFile("Resource/Models/SponzaCompressed/Sponza.gltf", new Transformation().WithScale(1.815f).WithTranslation(0.0f, -1.0f, 0.0f).GetMatrix()).Value;
            sponza.GpuModel.Meshes[63].EmissiveBias = 10.0f;
            sponza.GpuModel.Meshes[70].EmissiveBias = 20.0f;
            sponza.GpuModel.Meshes[3].EmissiveBias = 12.0f;
            sponza.GpuModel.Meshes[99].EmissiveBias = 15.0f;
            sponza.GpuModel.Meshes[97].EmissiveBias = 9.0f;
            sponza.GpuModel.Meshes[42].EmissiveBias = 20.0f;
            sponza.GpuModel.Meshes[38].EmissiveBias = 20.0f;
            sponza.GpuModel.Meshes[40].EmissiveBias = 20.0f;
            sponza.GpuModel.Meshes[42].EmissiveBias = 20.0f;
            //sponza.GpuModel.Meshes[46].SpecularBias = 1.0f;
            //sponza.GpuModel.Meshes[46].RoughnessBias = -0.436f; // -0.665
            //sponza.GpuModel.Meshes[46].NormalMapStrength = 0.0f;

            ModelLoader.Model lucy = ModelLoader.LoadGltfFromFile("Resource/Models/LucyCompressed/Lucy.gltf", new Transformation().WithScale(0.8f).WithRotationDeg(0.0f, 90.0f, 0.0f).WithTranslation(-1.68f, 2.3f, 0.0f).GetMatrix()).Value;
            lucy.GpuModel.Meshes[0].SpecularBias = -1.0f;
            lucy.GpuModel.Meshes[0].TransmissionBias = 0.98f;
            lucy.GpuModel.Meshes[0].IORBias = -0.326f;
            lucy.GpuModel.Meshes[0].AbsorbanceBias = new Vector3(0.81f, 0.18f, 0.0f);
            lucy.GpuModel.Meshes[0].RoughnessBias = -1.0f;
            lucy.GpuModel.Meshes[0].TintOnTransmissive = false;
            lucy.GpuModel.Materials[0].IsVolumetric = true;

            ModelLoader.Model helmet = ModelLoader.LoadGltfFromFile("Resource/Models/HelmetCompressed/Helmet.gltf", new Transformation().WithRotationDeg(0.0f, 45.0f, 0.0f).GetMatrix()).Value;

            //ModelLoader.Model test = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\Bistro\Bistro.glb").Value;
            //ModelLoader.Model test = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\SanMiguel\SanMiguel.gltf").Value;
            //ModelLoader.Model test = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\DC\HighPolyDragon.glb").Value;
            //ModelLoader.Model test = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\SponzaMergedRotated45.glb").Value;
            //ModelLoader.Model test = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\DC\DragonMerged.glb").Value;

            // Merging a model with many meshes into one can more than 2x Ray Tracing performance! (even with TLAS)
            ModelLoader.HoistMeshPrimitives(ref sponza, true);

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
        }
        else
        {
            ModelLoader.Model a = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Base\Compressed\NewSponza_Main_glTF_002.gltf").Value;
            //ModelLoader.Model b = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Curtains\Compressed\NewSponza_Curtains_glTF.gltf").Value;
            //ModelLoader.Model c = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Ivy\Compressed\NewSponza_IvyGrowth_glTF.gltf").Value;
            ////ModelLoader.Model d = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\IntelSponza\Tree\Compressed\NewSponza_CypressTree_glTF.gltf").Value;
            //ModelLoader.Model car = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\Sketchfab\free_-_mclaren_p1_mso\scene.gltf",
            //    new Transformation().WithTranslation(4.2f, 0.0f, 0.1f).WithScale(1.5f).WithRotationDeg(-180.0f, -73.0f, -180.0f).GetMatrix()
            //).Value;
            //ModelLoader.Model knight = ModelLoader.LoadGltfFromFile(@"C:\Users\Julian\Downloads\Models\Sketchfab\medieval_knight__sculpture__game_ready\scene.gltf",
            //    new Transformation().WithTranslation(-5.5f, 0.0f, 0.1f).WithScale(1.3f).WithRotationDeg(0.0f, 84.0f, 0.0f).GetMatrix()
            //).Value;

            ModelLoader.HoistMeshPrimitives(ref a);
            //ModelLoader.HoistMeshPrimitives(ref b);
            //ModelLoader.HoistMeshPrimitives(ref car);

            ModelManager.Add(a/*, b, c, car, knight*/);

            SetRenderMode(RenderMode.Rasterizer, WindowFramebufferSize, WindowFramebufferSize);

            //LightManager.AddLight(new CpuLight(new Vector3(-6.256f, 8.415f, -0.315f), new Vector3(820.0f, 560.0f, 586.0f), 0.3f));
            //LightManager.CreatePointShadowForLight(new CpuPointShadow(512, WindowFramebufferSize, new Vector2(0.1f, 60.0f)), 0);
        }

        MouseState.CursorMode = CursorModeValue.CursorNormal;
        FrameStateRecorder = new StateRecorder<FrameState>();
        WindowVSync = true;
        TimeEnabled = true;
    }

    protected override void OnWindowResize()
    {
        gui.SetSize(WindowFramebufferSize);

        // If GUI is used it calculates and sets the new (viewport-)resolution
        // If GUI is not used the new resolution is simply window size, that case is handled here
        if (!RenderImGui)
        {
            RequestPresentationResolution = WindowFramebufferSize;
        }

        OnRender(gpuPerFrameData.DeltaRenderTime);
    }

    protected override void OnKeyPress(uint key)
    {
        gui.PressChar(key);
    }

    protected override void OnFilesDrop(ReadOnlySpan<string> paths)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            string ext = Path.GetExtension(path);
            if (ext == ".gltf" || ext == ".glb")
            {
                gui.AddModelDialog(path);
            }
            else
            {
                Logger.Log(Logger.LogLevel.Warn, $"Dropped file \"{Path.GetFileName(path)}\" is unsupported. Only .gltf and .glb");
            }
        }
    }

    /// <summary>
    /// We should avoid random resolution changes inside a frame so if you can use
    /// <see cref="RequestPresentationResolution"/> instead.
    /// It will always make the change at the beginning of a frame.
    /// </summary>
    private void SetResolutions(Vector2i renderRes, Vector2i presentRes)
    {
        RasterizerPipeline?.SetSize(renderRes, presentRes);
        PathTracerPipeline?.SetSize(renderRes);

        if (RenderMode_ == RenderMode.Rasterizer || RenderMode_ == RenderMode.PathTracer)
        {
            if (VolumetricLight != null) VolumetricLight.SetSize(presentRes);
            if (Bloom != null) Bloom.SetSize(presentRes);
            if (TonemapAndGamma != null) TonemapAndGamma.SetSize(presentRes);
            if (LightManager != null) LightManager.SetSizeRayTracedShadows(renderRes);
        }
    }

    /// <summary>
    /// We should avoid random render mode changes inside a frame so if you can use
    /// <see cref="RequestRenderMode"/> instead.
    /// It will always make the change at the beginning of a frame.
    /// </summary>
    private void SetRenderMode(RenderMode renderMode, Vector2i renderRes, Vector2i presentRes)
    {
        // On AMD driver Vanguard-24.10-RC5-May22 and newer (and not 23.12.1 or earlier) setting the
        // Rasterizer render mode may cause a crash, especially during texture loading.

        if (renderMode == RenderMode.Rasterizer)
        {
            RasterizerPipeline?.Dispose();
            RasterizerPipeline = new RasterPipeline(renderRes, presentRes);
        }
        else
        {
            RasterizerPipeline?.Dispose();
            RasterizerPipeline = null;
        }

        if (renderMode == RenderMode.PathTracer)
        {
            // We disable time to allow for accumulation by default in case there were any moving objects
            TimeEnabled = false;

            PathTracerPipeline?.Dispose();
            PathTracerPipeline = new PathTracerPipeline(renderRes, PathTracerPipeline == null ? new PathTracer.GpuSettings() : PathTracerPipeline.GetPTSettings());
        }
        else
        {
            PathTracerPipeline?.Dispose();
            PathTracerPipeline = null;
        }

        if (renderMode == RenderMode.Rasterizer || renderMode == RenderMode.PathTracer)
        {
            if (BoxRenderer != null) BoxRenderer.Dispose();
            BoxRenderer = new BoxRenderer();

            if (TonemapAndGamma != null) TonemapAndGamma.Dispose();
            TonemapAndGamma = new TonemapAndGammaCorrect(presentRes, TonemapAndGamma == null ? new TonemapAndGammaCorrect.GpuSettings() : TonemapAndGamma.Settings);

            if (Bloom != null) Bloom.Dispose();
            Bloom = new Bloom(presentRes, Bloom == null ? new Bloom.GpuSettings() : Bloom.Settings);

            if (VolumetricLight != null) VolumetricLight.Dispose();
            VolumetricLight = new VolumetricLighting(presentRes, VolumetricLight == null ? new VolumetricLighting.GpuSettings() : VolumetricLight.Settings);
        }
    }

    public void SetFrameState(in FrameState state)
    {
        Camera.Position = state.CameraState.Position;
        Camera.UpVector = state.CameraState.UpVector;
        Camera.Yaw = state.CameraState.LookX;
        Camera.Pitch = state.CameraState.LookY;
        Camera.FovY = state.CameraState.FovY;
        animationTime = state.AnimationTime;
    }

    private FrameState GetFrameState()
    {
        FrameState state = new FrameState();
        state.CameraState.Position = Camera.Position;
        state.CameraState.UpVector = Camera.UpVector;
        state.CameraState.LookX = Camera.Yaw;
        state.CameraState.LookY = Camera.Pitch;
        state.CameraState.FovY = Camera.FovY;
        state.AnimationTime = animationTime;

        return state;
    }

    private void HandleFrameRecorderLogic()
    {
        if (RecorderVars.State == FrameRecorderState.Replaying)
        {
            if (RenderMode_ == RenderMode.Rasterizer ||
                (RenderMode_ == RenderMode.PathTracer && PathTracerPipeline.AccumulatedSamples >= RecorderVars.PathTracerSamples))
            {
                if (RecorderVars.IsOutputFrames)
                {
                    string path = $"{RecordingSettings.FRAMES_OUTPUT_FOLDER}/{FrameStateRecorder.ReplayStateIndex}";
                    Directory.CreateDirectory(RecordingSettings.FRAMES_OUTPUT_FOLDER);
                    Helper.TextureToDiskJpg(TonemapAndGamma.Result, path);
                }

                SetFrameState(FrameStateRecorder.Replay());

                // Stop replaying when we are at the first frame again
                if (FrameStateRecorder.ReplayStateIndex == 0)
                {
                    RecorderVars.State = FrameRecorderState.None;
                }
            }
        }
        else if (RecorderVars.State == FrameRecorderState.Recording)
        {
            if (RecorderVars.FrameTimer.Elapsed.TotalMilliseconds >= (1000.0f / RecorderVars.FPSGoal))
            {
                FrameStateRecorder.Record(GetFrameState());
                RecorderVars.FrameTimer.Restart();
            }
        }

        if (RecorderVars.State != FrameRecorderState.Replaying &&
            KeyboardState[Keys.R] == Keyboard.InputState.Touched &&
            KeyboardState[Keys.LeftControl] == Keyboard.InputState.Pressed)
        {
            // Start/Stop recording
            if (RecorderVars.State == FrameRecorderState.Recording)
            {
                RecorderVars.State = FrameRecorderState.None;
            }
            else
            {
                RecorderVars.State = FrameRecorderState.Recording;
                RecorderVars.FrameTimer.Restart();

                FrameStateRecorder.Clear();
            }
        }

        if (RecorderVars.State != FrameRecorderState.Recording &&
            KeyboardState[Keys.Space] == Keyboard.InputState.Touched &&
            KeyboardState[Keys.LeftControl] == Keyboard.InputState.Pressed)
        {
            // Start/Stop replaying
            if (RecorderVars.State == FrameRecorderState.Replaying)
            {
                RecorderVars.State = FrameRecorderState.None;
            }
            else if (FrameStateRecorder.Count > 0)
            {
                if (RenderMode_ == RenderMode.PathTracer)
                {
                    // Copy settings into PathTracer
                    PathTracerPipeline.DenoisingEnabled = RecorderVars.DoDenoising;

                    if (RecorderVars.DoDenoising)
                    {
                        PathTracerPipeline.AutoDenoiseSamplesThreshold = RecorderVars.PathTracerSamples;
                    }
                }

                RecorderVars.State = FrameRecorderState.Replaying;
                MouseState.CursorMode = CursorModeValue.CursorNormal;

                // Replay first frame here to avoid edge cases
                SetFrameState(FrameStateRecorder.Replay());
            }
        }
    }
}
