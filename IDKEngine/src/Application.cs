using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using IDKEngine.Render;
using IDKEngine.Render.Objects;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace IDKEngine
{
    /// <summary>
    /// This class represents the engine which can be run inside of an OpenGL context
    /// </summary>
    class Application : GameWindowBase
    {
        public Application(int width, int height, string title)
            : base(width, height, title)
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
                GLSLBasicData.FreezeFramesCounter = 0;

                // TODO: Add comment as to why we do that here lol
                int i = 0;
                ModelSystem.UpdateDrawCommandBuffer(0, ModelSystem.DrawCommands.Length, (ref GLSLDrawCommand cmd) =>
                {
                    cmd.InstanceCount = ModelSystem.Meshes[i].InstanceCount;
                });
                PathTracer.Result.Clear(PixelFormat.Rgba, PixelType.Float, 0.0f);
            }

        }

        public bool IsVolumetricLighting = true, IsSSAO = true, IsSSR = false, IsBloom = true, IsShadows = true, IsVRSForwardRender = false;
        public int FPS;
        public Vector2i ViewportSize { get; private set; }

        private bool renderGui = true;
        private int fps;
        protected override unsafe void OnRender(float dT)
        {
            GLSLBasicData.DeltaUpdate = dT;
            GLSLBasicData.PrevProjView = GLSLBasicData.ProjView;
            GLSLBasicData.ProjView = camera.View * GLSLBasicData.Projection;
            GLSLBasicData.View = camera.View;
            GLSLBasicData.InvView = camera.View.Inverted();
            GLSLBasicData.CameraPos = camera.Position;
            GLSLBasicData.InvProjView = (GLSLBasicData.View * GLSLBasicData.Projection).Inverted();
            basicDataUBO.SubData(0, sizeof(GLSLBasicData), GLSLBasicData);

            if (!IsPathTracing)
            {
                // Compute last frames SSAO
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

                ModelSystem.ViewCull(ref GLSLBasicData.ProjView);

                GL.Viewport(0, 0, ForwardRenderer.Result.Width, ForwardRenderer.Result.Height);

                if (IsVRSForwardRender)
                    VariableRateShading.IsEnabled = true;

                ForwardRenderer.Render(ModelSystem, AtmosphericScatterer.Result, IsSSAO ? SSAO.Result : null);
                VariableRateShading.IsEnabled = false;

                if (IsBloom)
                    Bloom.Compute(ForwardRenderer.Result);

                if (IsVolumetricLighting)
                    VolumetricLight.Compute(ForwardRenderer.DepthTexture);

                if (IsSSR)
                    SSR.Compute(ForwardRenderer.Result, ForwardRenderer.NormalSpecTexture, ForwardRenderer.DepthTexture, AtmosphericScatterer.Result);

                // Small "hack" to enable VRS debug image even on systems that don't support the extension
                if (VariableRateShading.NV_SHADING_RATE_IMAGE)
                {
                    if (IsVRSForwardRender)
                        ForwardPassVRS.Compute(ForwardRenderer.Result, ForwardRenderer.VelocityTexture);
                }
                else if (ForwardPassVRS.DebugValue != VariableRateShading.DebugMode.NoDebug)
                {
                    ForwardPassVRS.Compute(ForwardRenderer.Result, ForwardRenderer.VelocityTexture);
                }
                PostCombine.Compute(ForwardRenderer.Result, IsBloom ? Bloom.Result : null, IsVolumetricLighting ? VolumetricLight.Result : null, IsSSR ? SSR.Result : null);
            }
            else
            {
                PathTracer.Compute();
                Texture.UnbindFromUnit(1);
                Texture.UnbindFromUnit(2);

                if (IsBloom)
                    Bloom.Compute(PathTracer.Result);

                PostCombine.Compute(PathTracer.Result, IsBloom ? Bloom.Result : null, null, null);
            }

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);

            GL.Viewport(0, 0, WindowSize.X, WindowSize.Y);
            Framebuffer.Bind(0);
            GLSLBasicData.FreezeFramesCounter++;

            if (renderGui)
            {
                gui.Draw(this, (float)dT);
            }
            else
            {
                PostCombine.Result.BindToUnit(0);
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
                WindowTitle = $"FPS: {FPS}; Position {camera.Position};";
                fps = 0;
                fpsTimer.Restart();
            }

            if (KeyboardState[Keys.Escape] == InputState.Pressed)
                ShouldClose();
                
            if (KeyboardState[Keys.V] == InputState.Touched)
                WindowVSync = !WindowVSync;

            if (KeyboardState[Keys.F11] == InputState.Touched)
                WindowFullscreen = !WindowFullscreen;

            if (KeyboardState[Keys.G] == InputState.Touched)
            {
                renderGui = !renderGui;
                if (!renderGui)
                {
                    SetViewportSize(WindowSize.X, WindowSize.Y);
                }
            }

            if (KeyboardState[Keys.E] == InputState.Touched && !ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
            {
                if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
                {
                    MouseState.CursorMode = CursorModeValue.CursorNormal;
                    gui.ImGuiBackend.IsIgnoreMouseInput = false;
                    camera.Velocity = Vector3.Zero;
                }
                else
                {
                    MouseState.CursorMode = CursorModeValue.CursorDisabled;
                    gui.ImGuiBackend.IsIgnoreMouseInput = true;
                }
            }

            if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
            {
                camera.ProcessInputs(KeyboardState, MouseState, dT, out bool hadCameraInputs);
                if (hadCameraInputs)
                    GLSLBasicData.FreezeFramesCounter = 0;
            }

            gui.Update(this);
        }

        private Camera camera;
        private BufferObject basicDataUBO;
        private List<PointShadow> pointShadows;
        public ShaderProgram FinalProgram;
        public ModelSystem ModelSystem;
        public Forward ForwardRenderer;
        public Bloom Bloom;
        public VariableRateShading ForwardPassVRS;
        public SSR SSR;
        public SSAO SSAO;
        public PostCombine PostCombine;
        public VolumetricLighter VolumetricLight;
        public AtmosphericScatterer AtmosphericScatterer;
        public PathTracer PathTracer;
        public BVH BVH;
        private Gui gui;
        public GLSLBasicData GLSLBasicData;
        protected override unsafe void OnStart()
        {
            Console.WriteLine($"API: {GL.GetString(StringName.Version)}");
            Console.WriteLine($"GPU: {GL.GetString(StringName.Renderer)}\n\n");

            if (!Helper.IsExtensionsAvailable("GL_ARB_bindless_texture"))
            {
                Console.WriteLine("Your system does not support GL_ARB_bindless_texture");
                Console.ReadLine();
                throw new NotSupportedException();
            }

            GL.PointSize(1.3f);
            GL.Enable(EnableCap.TextureCubeMapSeamless);
            GL.Enable(EnableCap.DepthTest);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
#if DEBUG
            GL.Enable(EnableCap.DebugOutputSynchronous);
            GL.DebugMessageCallback(Helper.DebugCallback, IntPtr.Zero);
#endif
            WindowVSync = true;
            MouseState.CursorMode = CursorModeValue.CursorDisabled;
            gui = new Gui(WindowSize.X, WindowSize.Y);
            gui.ImGuiBackend.IsIgnoreMouseInput = true;

            camera = new Camera(new Vector3(6.252f, 9.49f, -1.96f), new Vector3(0.0f, 1.0f, 0.0f), -183.5f, 0.5f, 0.1f, 0.25f);
            //camera = new Camera(new Vector3(-8.0f, 2.00f, -0.5f), new Vector3(0.0f, 1.0f, 0.0f), -183.5f, 0.5f, 0.1f, 0.25f);

            Model sponza = new Model("res/models/OBJSponza/sponza.obj");
            for (int i = 0; i < sponza.ModelMatrices.Length; i++) // 0.0145f
                sponza.ModelMatrices[i][0] = Matrix4.CreateScale(5.0f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f);

            Model horse = new Model("res/models/Horse/horse.gltf");
            for (int i = 0; i < horse.ModelMatrices.Length; i++)
                horse.ModelMatrices[i][0] = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(120.0f)) * Matrix4.CreateScale(25.0f) * Matrix4.CreateTranslation(-12.0f, -1.05f, -0.5f);

            //Model temple = new Model(@"C:\Users\Julian\Downloads\SunTempleSmall\SunTempleSmall.gltf");
            //for (int i = 0; i < temple.ModelMatrices.Length; i++)
            //    temple.ModelMatrices[i][0] = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(180.0f)) * Matrix4.CreateScale(0.01f) * Matrix4.CreateTranslation(-12.0f, -1.05f, -0.5f);

            ModelSystem = new ModelSystem();
            ModelSystem.Add(new Model[] { sponza, horse });

            {
                IsVRSForwardRender = VariableRateShading.NV_SHADING_RATE_IMAGE;
                Span<NvShadingRateImage> shadingRates = stackalloc NvShadingRateImage[]
                {
                    NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                    NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                    NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                    NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                    NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
                };
                
                string srcCode = File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl");
                int effectiveSubGroupSize = 1;
                if (Helper.IsExtensionsAvailable("GL_KHR_shader_subgroup"))
                {
                    SubgroupSupportedFeatures bitfield = (SubgroupSupportedFeatures)GL.GetInteger(GetPName.SubgroupSupportedFeaturesKhr);
                    if ((bitfield & SubgroupSupportedFeatures.SubgroupFeatureArithmeticBitKhr) == SubgroupSupportedFeatures.SubgroupFeatureArithmeticBitKhr)
                    {
                        effectiveSubGroupSize = GL.GetInteger(GetPName.SubgroupSizeKhr);
                    }
                }
                srcCode = srcCode.Replace("__effectiveSubroupSize__", Convert.ToString(effectiveSubGroupSize));
                
                ForwardPassVRS = new VariableRateShading(new Shader(ShaderType.ComputeShader, srcCode), WindowSize.X, WindowSize.Y);
                VariableRateShading.BindVRSNV(ForwardPassVRS);
                VariableRateShading.SetShadingRatePaletteNV(shadingRates);
            }
            
            ForwardRenderer = new Forward(new Lighter(20, 20), WindowSize.X, WindowSize.Y, 6);
            Bloom = new Bloom(WindowSize.X, WindowSize.Y, 1.0f, 3.0f);
            SSR = new SSR(WindowSize.X, WindowSize.Y, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(WindowSize.X, WindowSize.Y, 14, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));
            SSAO = new SSAO(WindowSize.X, WindowSize.Y, 16, 0.25f, 2.0f);
            PostCombine = new PostCombine(WindowSize.X, WindowSize.Y);
            AtmosphericScatterer = new AtmosphericScatterer(256);
            AtmosphericScatterer.Compute();

            Stopwatch timer = Stopwatch.StartNew();
            BVH = new BVH(ModelSystem);
            timer.Stop();
            Console.WriteLine($"BVH build time: {timer.ElapsedMilliseconds / 1000.0f} sec");

            PathTracer = new PathTracer(BVH, ModelSystem, AtmosphericScatterer.Result, WindowSize.X, WindowSize.Y);
            /// Driver bug: Global seamless cubemap feature may be ignored when sampling from uniform samplerCube
            /// in Compute Shader with ARB_bindless_texture activated. So try switching to seamless_cubemap_per_texture
            /// More info: https://stackoverflow.com/questions/68735879/opengl-using-bindless-textures-on-sampler2d-disables-texturecubemapseamless
            if (Helper.IsExtensionsAvailable("GL_AMD_seamless_cubemap_per_texture") || Helper.IsExtensionsAvailable("GL_ARB_seamless_cubemap_per_texture"))
                AtmosphericScatterer.Result.SetSeamlessCubeMapPerTextureARB_AMD(true);

            List<GLSLLight> lights = new List<GLSLLight>();
            //lights.Add(new GLSLLight(new Vector3(-0.5f, 8.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 1.0f));
            lights.Add(new GLSLLight(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(3.5f, 0.8f, 0.9f) * 6.3f, 0.3f));
            lights.Add(new GLSLLight(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 0.3f));
            lights.Add(new GLSLLight(new Vector3(4.5f, 5.7f, -2.0f), new Vector3(0.5f, 0.8f, 3.9f) * 6.3f, 0.3f));
            ForwardRenderer.LightingContext.Add(lights.ToArray());
            
            pointShadows = new List<PointShadow>();
            for (int i = 0; i < lights.Count; i++)
            {
                pointShadows.Add(new PointShadow(ForwardRenderer.LightingContext, i, 512, 0.5f, 60.0f));
            }

            basicDataUBO = new BufferObject();
            basicDataUBO.ImmutableAllocate(sizeof(GLSLBasicData), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            basicDataUBO.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, 0, basicDataUBO.Size);

            Image<Rgba32> img = SixLabors.ImageSharp.Image.Load<Rgba32>("res/textures/blueNoise/LDR_RGBA_1024.png");
            Texture blueNoise = new Texture(TextureTarget2d.Texture2D);
            blueNoise.ImmutableAllocate(img.Width, img.Height, 1, SizedInternalFormat.Rgba8);
            blueNoise.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            blueNoise.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            fixed (void* ptr = img.GetPixelRowSpan(0))
            {
                blueNoise.SubTexture2D(img.Width, img.Height, PixelFormat.Rgba, PixelType.UnsignedByte, (IntPtr)ptr);
            }
            
            BufferObject blueNoiseUBO = new BufferObject();
            blueNoiseUBO.ImmutableAllocate(sizeof(long), blueNoise.MakeImageHandleResidentARB(0, false, 0, SizedInternalFormat.Rgba8, TextureAccess.ReadOnly), BufferStorageFlags.DynamicStorageBit);
            blueNoiseUBO.BindBufferRange(BufferRangeTarget.UniformBuffer, 4, 0, blueNoiseUBO.Size);

            FinalProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/fragment.glsl")));

            gui.ImGuiBackend.WindowResized(WindowSize.X, WindowSize.Y);
            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), WindowSize.X / (float)WindowSize.Y, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;

            GC.Collect();
        }


        protected override void OnResize()
        {
            gui.ImGuiBackend.WindowResized(WindowSize.X, WindowSize.Y);

            // if we don't render to the screen via gui always make viewport match window size
            if (!renderGui)
            {
                SetViewportSize(WindowSize.X, WindowSize.Y);
            }
        }

        public void SetViewportSize(int width, int height)
        {
            if (width < 16 || height < 16)
                return;

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
            PostCombine.SetSize(width, height);
            PathTracer.SetSize(width, height);
            
            GLSLBasicData.FreezeFramesCounter = 0;

            ViewportSize = new Vector2i(width, height);
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
