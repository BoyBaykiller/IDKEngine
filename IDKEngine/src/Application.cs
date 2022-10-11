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
    class Application : GameWindowBase
    {
        public Application(int width, int height, string title)
            : base(width, height, title, 4, 6)
        {

        }

        public const float EPSILON = 0.001f;
        public const float NEAR_PLANE = 0.01f, FAR_PLANE = 500.0f;

        private bool _isPathTracing;
        public bool IsPathTracing
        {
            get => _isPathTracing;

            set
            {
                _isPathTracing = value;
                PathTracer.ResetRender();

                float clearData = 0.0f;
                PathTracer.Result.Clear(PixelFormat.Rgba, PixelType.Float, ref clearData);
            }

        }

        public bool IsVolumetricLighting = true, IsSSAO = true, IsSSR = false, IsBloom = true, IsShadows = true, IsVRSForwardRender = false, IsWireframe = false;
        public int FPS;

        public Vector2i ViewportResolution { get; private set; }

        public bool RenderGui { get; private set; } = true;
        private int fps;
        protected override unsafe void OnRender(float dT)
        {
            GLSLBasicData.DeltaUpdate = dT;
            GLSLBasicData.PrevProjView = GLSLBasicData.ProjView;
            GLSLBasicData.ProjView = Camera.View * GLSLBasicData.Projection;
            GLSLBasicData.View = Camera.View;
            GLSLBasicData.InvView = Camera.View.Inverted();
            GLSLBasicData.CameraPos = Camera.Position;
            GLSLBasicData.InvProjView = (GLSLBasicData.View * GLSLBasicData.Projection).Inverted();
            GLSLBasicData.Time = WindowTime;
            if (GLSLBasicData.PrevProjView != GLSLBasicData.ProjView)
            {
                PathTracer.ResetRender();
            }
            basicDataUBO.SubData(0, sizeof(GLSLBasicData), GLSLBasicData);

            if (!IsPathTracing)
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
                    GL.ColorMask(false, false, false, false);
                    for (int i = 0; i < pointShadows.Count; i++)
                    {
                        pointShadows[i].CreateDepthMap(ModelSystem);
                    }
                    GL.ColorMask(true, true, true, true);
                }

                ModelSystem.FrustumCull(ref GLSLBasicData.ProjView);

                GL.Viewport(0, 0, ForwardRenderer.Result.Width, ForwardRenderer.Result.Height);

                if (IsVRSForwardRender)
                    VariableRateShading.IsEnabled = true;

                ForwardRenderer.Render(ModelSystem, IsSSAO ? SSAO.Result : null);
                VariableRateShading.IsEnabled = false;

                if (IsBloom)
                    Bloom.Compute(ForwardRenderer.Result);

                if (IsVolumetricLighting)
                    VolumetricLight.Compute(ForwardRenderer.DepthTexture);

                if (IsSSR)
                    SSR.Compute(ForwardRenderer.Result, ForwardRenderer.NormalSpecTexture, ForwardRenderer.DepthTexture);

                PostProcessor.CombineCompute(ForwardRenderer.Result, IsBloom ? Bloom.Result : null, IsVolumetricLighting ? VolumetricLight.Result : null, IsSSR ? SSR.Result : null);
                PostProcessor.TaaCompute(ForwardRenderer.VelocityTexture, ForwardRenderer.DepthTexture);
                if (VariableRateShading.NV_SHADING_RATE_IMAGE)
                {
                    if (IsVRSForwardRender)
                        ForwardPassVRS.Compute(PostProcessor.Result, ForwardRenderer.VelocityTexture);
                }
                // Small "hack" to enable VRS debug image even on systems that don't support the extension
                else if (ForwardPassVRS.DebugValue != VariableRateShading.DebugMode.NoDebug)
                {
                    ForwardPassVRS.Compute(PostProcessor.Result, ForwardRenderer.VelocityTexture);
                }

                if (IsWireframe)
                {
                    GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                }
            }
            else
            {
                PathTracer.Compute();

                if (IsBloom)
                    Bloom.Compute(PathTracer.Result);

                PostProcessor.CombineCompute(PathTracer.Result, IsBloom ? Bloom.Result : null, null, null);
            }

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
        private List<PointShadow> pointShadows;
        public ShaderProgram FinalProgram;
        public ModelSystem ModelSystem;
        public ForwardRenderer ForwardRenderer;
        public Bloom Bloom;
        public VariableRateShading ForwardPassVRS;
        public SSR SSR;
        public SSAO SSAO;
        public PostProcessor PostProcessor;
        public VolumetricLighter VolumetricLight;
        public PathTracer PathTracer;
        public BVH BVH;
        private Gui gui;
        public GLSLBasicData GLSLBasicData;
        public FrameRecorder<RecordableState> FrameRecorder;
        protected override unsafe void OnStart()
        {
            Console.WriteLine($"API: {GL.GetString(StringName.Version)}");
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}\n\n");

            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
            {
                Console.WriteLine("Your system does not support GL_ARB_bindless_texture. Press Enter to exit");
                Console.ReadLine();
                Environment.Exit(1);
            }

            GL.PointSize(1.3f);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.ScissorTest);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
#if DEBUG
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageCallback(Helper.DebugCallback, (IntPtr)0);
#endif
            WindowVSync = true;
            MouseState.CursorMode = CursorModeValue.CursorDisabled;
            gui = new Gui(WindowSize.X, WindowSize.Y);

            gui.ImGuiBackend.WindowResized(WindowSize.X, WindowSize.Y);
            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), WindowSize.X / (float)WindowSize.Y, NEAR_PLANE, FAR_PLANE);
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
            sponza.Meshes[8].SpecularBias = -0.65f;
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

            Span<NvShadingRateImage> shadingRates = stackalloc NvShadingRateImage[]
            {
                NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
            };
            VariableRateShading.SetShadingRatePaletteNV(shadingRates);
                
            ForwardPassVRS = new VariableRateShading(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl")), WindowSize.X, WindowSize.Y);
            VariableRateShading.BindVRSNV(ForwardPassVRS);

            ForwardRenderer = new ForwardRenderer(new Lighter(12, 12), WindowSize.X, WindowSize.Y, 6);
            Bloom = new Bloom(WindowSize.X, WindowSize.Y, 1.0f, 3.0f);
            SSR = new SSR(WindowSize.X, WindowSize.Y, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(WindowSize.X, WindowSize.Y, 7, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));
            SSAO = new SSAO(WindowSize.X, WindowSize.Y, 10, 0.1f, 2.0f);
            PostProcessor = new PostProcessor(WindowSize.X, WindowSize.Y);

            BVH = new BVH(ModelSystem);
            PathTracer = new PathTracer(BVH, ModelSystem, WindowSize.X, WindowSize.Y);

            List<GLSLLight> lights = new List<GLSLLight>();
            //lights.Add(new GLSLLight(new Vector3(-6.0f, 21.0f, 2.95f), new Vector3(4.585f, 4.725f, 2.56f) * 10.0f, 1.0f));
            lights.Add(new GLSLLight(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(3.5f, 0.8f, 0.9f) * 6.3f, 0.3f));
            lights.Add(new GLSLLight(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 0.3f));
            lights.Add(new GLSLLight(new Vector3(4.5f, 5.7f, -2.0f), new Vector3(0.5f, 0.8f, 3.9f) * 6.3f, 0.3f));
            ForwardRenderer.LightingContext.Add(CollectionsMarshal.AsSpan(lights));

            pointShadows = new List<PointShadow>();
            for (int i = 0; i < 3; i++)
            {
                pointShadows.Add(new PointShadow(ForwardRenderer.LightingContext, i, 512, 0.5f, 60.0f));
            }

            FrameRecorder = new FrameRecorder<RecordableState>();

            MouseState.CursorMode = CursorModeValue.CursorNormal;
            IsPathTracing = false;

            GC.Collect();
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
            ForwardRenderer.SetSize(width, height);
            ForwardPassVRS.SetSize(width, height);
            Bloom.SetSize(width, height);
            VolumetricLight.SetSize(width, height);
            SSR.SetSize(width, height);
            SSAO.SetSize(width, height);
            PostProcessor.SetSize(width, height);
            PathTracer.SetSize(width, height);
        }

        protected override void OnEnd()
        {

        }

        protected override void OnKeyPress(char key)
        {
            gui.ImGuiBackend.PressChar(key);
        }
    }
}
