using System;
using System.IO;
using System.Diagnostics;
using OpenTK;
using OpenTK.Input;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class Window : GameWindow
    {
        public const float EPSILON = 0.001f;
        public const float NEAR_PLANE = 0.01f, FAR_PLANE = 500.0f;

        public Window()
#if DEBUG
            : base(832, 832, new GraphicsMode(0, 0, 0, 0), string.Empty, GameWindowFlags.Default, DisplayDevice.Default, 4, 6, GraphicsContextFlags.Debug)
#else
            : base(832, 832, new GraphicsMode(0, 0, 0, 0))
#endif
        {

        }

        private readonly Camera camera = new Camera(new Vector3(0.0f, 5.0f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f), -90.0f, 0.0f, 0.1f, 0.25f);


        public bool IsPathTracing = false, IsVolumetricLighting = true, IsSSAO = true, IsSSR = false;
        public int FPS;
        private int fps;
        protected override unsafe void OnRenderFrame(FrameEventArgs e)
        {
            basicDataUBO.SubData(0, sizeof(GLSLBasicData), GLSLBasicData);

            if (!IsPathTracing)
            {
                // Compute last frames SSAO
                if (IsSSAO)
                    SSAO.Compute(ForwardRenderer.Depth, ForwardRenderer.NormalSpec);

                // 1. If IS_VERTEX_LAYERED_RENDERING is false
                //    upload unculled command buffer for shadows to avoid supplying player-culled command buffer for shadows
                if (!PointShadow.IS_VERTEX_LAYERED_RENDERING)
                {
                    ModelSystem.DrawCommandBuffer.SubData(0, ModelSystem.DrawCommandBuffer.Size, ModelSystem.DrawCommands);
                }
                
                for (int i = 0; i < pointShadows.Length; i++)
                {
                    pointShadows[i].CreateDepthMap(ModelSystem);
                }

                ModelSystem.ViewCull(ref GLSLBasicData.ProjView);

                GL.Viewport(0, 0, Width, Height);
                ForwardRenderer.Render(ModelSystem, AtmosphericScatterer.Result, IsSSAO ? SSAO.Result : null);

                if (IsVolumetricLighting)
                    VolumetricLight.Compute(ForwardRenderer.Depth);

                if (IsSSR)
                    SSR.Compute(ForwardRenderer.Result, ForwardRenderer.NormalSpec, ForwardRenderer.Depth, AtmosphericScatterer.Result);

                PostCombine.Compute(ForwardRenderer.Result, IsVolumetricLighting ? VolumetricLight.Result : null, IsSSR ? SSR.Result : null);
                PostCombine.Result.BindToUnit(0);
            }
            else
            {
                PathTracer.Render();
                Texture.UnbindFromUnit(1);
                Texture.UnbindFromUnit(2);
                PathTracer.Result.BindToUnit(0);
            }

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            GL.Viewport(0, 0, Width, Height);
            Framebuffer.Bind(0);
            finalProgram.Use();

            GL.DrawArrays(PrimitiveType.Quads, 0, 4);
            Gui.Render(this, (float)e.Time);
            
            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);

            fps++;
            GLSLBasicData.FrameCount++;
            SwapBuffers();
            
            base.OnRenderFrame(e);
        }

        private readonly Stopwatch fpsTimer = Stopwatch.StartNew();
        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            if (fpsTimer.ElapsedMilliseconds >= 1000)
            {
                FPS = fps;
                Title = $"FPS: {FPS}; Position {camera.Position};";
                fps = 0;
                fpsTimer.Restart();
            }

            if (Focused)
            {
                ThreadManager.InvokeQueuedActions();
                
                KeyboardManager.Update();
                MouseManager.Update();

                if (KeyboardManager.IsKeyDown(Key.Escape))
                    Close();

                if (KeyboardManager.IsKeyTouched(Key.V))
                    VSync = VSync == VSyncMode.Off ? VSyncMode.On : VSyncMode.Off;

                if (KeyboardManager.IsKeyTouched(Key.F11))
                    WindowState = WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;

                if (ImGuiNET.ImGui.GetIO().WantCaptureMouse && !CursorVisible)
                {
                    System.Drawing.Point point = PointToScreen(new System.Drawing.Point(Width / 2, Height / 2));
                    Mouse.SetPosition(point.X, point.Y);
                }

                if (KeyboardManager.IsKeyTouched(Key.E) && !ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
                {
                    CursorVisible = !CursorVisible;
                    CursorGrabbed = !CursorGrabbed;

                    if (!CursorGrabbed)
                    {
                        CursorVisible = true;
                        MouseManager.Update();
                        camera.Velocity = Vector3.Zero;
                    }
                }

                if (!CursorVisible)
                {
                    camera.ProcessInputs((float)e.Time, out bool hadCameraInputs);
                    if (hadCameraInputs && IsPathTracing)
                        GLSLBasicData.FrameCount = 0;
                }

                if (CursorVisible)
                {
                    Gui.Update(this);
                }

                GLSLBasicData.PrevProjView = GLSLBasicData.View * GLSLBasicData.Projection;
                GLSLBasicData.ProjView = camera.View * GLSLBasicData.Projection;
                GLSLBasicData.View = camera.View;
                GLSLBasicData.InvView = camera.View.Inverted();
                GLSLBasicData.CameraPos = camera.Position;
                GLSLBasicData.InvProjView = (GLSLBasicData.View * GLSLBasicData.Projection).Inverted();
            }

            base.OnUpdateFrame(e);
        }

        private ShaderProgram finalProgram;
        private BufferObject basicDataUBO;
        private PointShadow[] pointShadows;
        public ModelSystem ModelSystem;
        public Forward ForwardRenderer;
        public SSR SSR;
        public SSAO SSAO;
        public PostCombine PostCombine;
        public BVH Bvh;
        public VolumetricLighter VolumetricLight;
        public GaussianBlur GaussianBlur;
        public AtmosphericScatterer AtmosphericScatterer;
        public PathTracer PathTracer;
        public GLSLBasicData GLSLBasicData;
        protected override unsafe void OnLoad(EventArgs e)
        {
            Console.WriteLine($"API: {GL.GetString(StringName.Version)}");
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}\n\n");

            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
                throw new NotSupportedException("Your system does not support GL_ARB_bindless_texture");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_shader_draw_parameters", 4.6))
                throw new NotSupportedException("Your system does not support GL_ARB_shader_draw_parameters");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_direct_state_access", 4.5))
                throw new NotSupportedException("Your system does not support GL_ARB_direct_state_access");

            if (!Helper.IsCoreExtensionAvailable("GL_ARB_buffer_storage", 4.4))
                throw new NotSupportedException("Your system does not support GL_ARB_buffer_storage");


            GL.LineWidth(1.1f);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
#if DEBUG
            GL.Enable(EnableCap.DebugOutput);
            GL.DebugMessageCallback(Helper.DebugCallback, IntPtr.Zero);
#endif
            VSync = VSyncMode.On;
            CursorGrabbed = true;
            CursorVisible = false;

            Model sponza = new Model("res/models/OBJSponza/sponza.obj");
            for (int i = 0; i < sponza.Meshes.Length; i++)
                sponza.Meshes[i].Model = Matrix4.CreateScale(5.0f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f);

            Model horse = new Model("res/models/Horse/horse.gltf");
            for (int i = 0; i < horse.Meshes.Length; i++)
                horse.Meshes[i].Model = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(120.0f)) * Matrix4.CreateScale(25.0f) * Matrix4.CreateTranslation(-12.0f, -1.05f, 0.5f);

            ModelSystem = new ModelSystem();
            ModelSystem.Add(new Model[] { sponza, horse });

            GLSLLight[] lights = new GLSLLight[2];
            lights[0] = new GLSLLight(new Vector3(-6.0f, 21.0f, 2.95f), new Vector3(4.585f, 4.725f, 2.56f) * 900.0f, 1.0f);
            //lights[0] = new GLSLLight(new Vector3(-6.0f, 21.0f, -0.95f), new Vector3(4.585f, 4.725f, 2.56f) * 900.0f, 0.2f);
            lights[1] = new GLSLLight(new Vector3(-14.0f, 4.7f, 1.0f), new Vector3(0.5f, 0.8f, 0.9f) * 40.0f, 0.5f);

            ForwardRenderer = new Forward(new Lighter(20, 20), Width, Height);
            ForwardRenderer.LightingContext.Add(lights);
            SSR = new SSR(Width, Height, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(Width, Height, 20, 0.758f, 50.0f, new Vector3(0.025f));
            GaussianBlur = new GaussianBlur(Width, Height);
            SSAO = new SSAO(Width, Height, 10, 0.3f);
            PostCombine = new PostCombine(Width, Height);
            AtmosphericScatterer = new AtmosphericScatterer(256);
            AtmosphericScatterer.Compute();
            /// Driver bug: Global seamless cubemap feature may be ignored when sampling from uniform samplerCube
            /// in Compute Shader with ARB_bindless_texture activated. So try switching to seamless_cubemap_per_texture
            /// More info: https://stackoverflow.com/questions/68735879/opengl-using-bindless-textures-on-sampler2d-disables-texturecubemapseamless
            if (Helper.IsExtensionsAvailable("GL_AMD_seamless_cubemap_per_texture") || Helper.IsExtensionsAvailable("GL_ARB_seamless_cubemap_per_texture"))
                AtmosphericScatterer.Result.SetSeamlessCubeMapPerTexture(true);

            pointShadows = new PointShadow[2];
            pointShadows[0] = new PointShadow(ForwardRenderer.LightingContext, 0, 1536, 1.0f, 60.0f);
            pointShadows[1] = new PointShadow(ForwardRenderer.LightingContext, 1, 256, 0.5f, 60.0f);

            pointShadows[0].CreateDepthMap(ModelSystem);
            pointShadows[1].CreateDepthMap(ModelSystem);

            Bvh = new BVH(ModelSystem);
            PathTracer = new PathTracer(Bvh, ModelSystem, AtmosphericScatterer.Result, Width, Height);

            basicDataUBO = new BufferObject();
            basicDataUBO.ImmutableAllocate(sizeof(GLSLBasicData), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            basicDataUBO.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, 0, basicDataUBO.Size);

            finalProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/fragment.glsl")));

            base.OnLoad(e);
        }

        private int lastWidth, lastHeight;
        protected override unsafe void OnResize(EventArgs e)
        {
            if ((lastWidth != Width || lastHeight != Height) && Width != 0 && Height != 0)
            {
                Gui.ImGuiController.WindowResized(Width, Height);

                GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), Width / (float)Height, NEAR_PLANE, FAR_PLANE);
                GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
                GLSLBasicData.NearPlane = NEAR_PLANE;
                GLSLBasicData.FarPlane = FAR_PLANE;

                ForwardRenderer.SetSize(Width, Height);
                VolumetricLight.SetSize(Width, Height);
                GaussianBlur.SetSize(Width, Height);
                SSR.SetSize(Width, Height);
                SSAO.SetSize(Width, Height);
                PostCombine.SetSize(Width, Height);
                if (IsPathTracing)
                {
                    PathTracer.SetSize(Width, Height);
                    GLSLBasicData.FrameCount = 0;
                }

                lastWidth = Width;
                lastHeight = Height;
            }
            
            base.OnResize(e);
        }

        protected override void OnFocusedChanged(EventArgs e)
        {
            if (Focused)
                MouseManager.Update();
            base.OnFocusedChanged(e);
        }
    }
}
