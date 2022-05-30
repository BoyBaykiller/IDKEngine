using System;
using System.IO;
using System.Diagnostics;
using IDKEngine.Render;
using IDKEngine.Render.Objects;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;

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

        public bool IsPathTracing = false, IsVolumetricLighting = true, IsSSAO = true, IsSSR = false, IsBloom = true, IsDithering = true, IsShadows = true, IsVRSForwardRender = false;
        public int FPS;

        private int fps;
        protected override unsafe void OnRender(float dT)
        {
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

                GL.Viewport(0, 0, Size.X, Size.Y);

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

            GL.Viewport(0, 0, Size.X, Size.Y);
            Framebuffer.Bind(0);

            PostCombine.Result.BindToUnit(0);

            FinalProgram.Use();

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            
            GLSLBasicData.FreezeFramesCounter++;
            gui.Draw(this, (float)dT);

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
                Title = $"FPS: {FPS}; Position {camera.Position};";
                fps = 0;
                fpsTimer.Restart();
            }

            if (KeyboardState[Keys.Escape] == InputState.Pressed)
                ShouldClose();
                
            if (KeyboardState[Keys.V] == InputState.Touched)
                IsVSync = !IsVSync;

            if (KeyboardState[Keys.F11] == InputState.Touched)
                IsFullscreen = !IsFullscreen;

            if (KeyboardState[Keys.E] == InputState.Touched && !ImGuiNET.ImGui.GetIO().WantCaptureKeyboard)
            {
                if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
                {
                    MouseState.CursorMode = CursorModeValue.CursorNormal;
                    gui.ImGuiController.IsIgnoreMouseInput = false;
                    camera.Velocity = Vector3.Zero;
                }
                else
                {
                    MouseState.CursorMode = CursorModeValue.CursorDisabled;
                    gui.ImGuiController.IsIgnoreMouseInput = true;
                }
            }

            if (MouseState.CursorMode == CursorModeValue.CursorDisabled)
            {
                camera.ProcessInputs(KeyboardState, MouseState, dT, out bool hadCameraInputs);
                if (hadCameraInputs)
                    GLSLBasicData.FreezeFramesCounter = 0;
            }
            GLSLBasicData.DeltaUpdate = dT;

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
        public BVH Bvh;
        public VolumetricLighter VolumetricLight;
        public AtmosphericScatterer AtmosphericScatterer;
        public PathTracer PathTracer;
        public Gui gui;
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
            IsVSync = true;
            MouseState.CursorMode = CursorModeValue.CursorDisabled;
            gui = new Gui(Size.X, Size.Y);
            gui.ImGuiController.IsIgnoreMouseInput = true;

            camera = new Camera(new Vector3(6.252f, 9.49f, -1.96f), new Vector3(0.0f, 1.0f, 0.0f), -183.5f, 0.5f, 0.1f, 0.25f);

            Model sponza = new Model("res/models/OBJSponza/sponza.obj");
            for (int i = 0; i < sponza.Models.Length; i++) // 0.0145f
                sponza.Models[i][0] = Matrix4.CreateScale(5.0f) * Matrix4.CreateTranslation(0.0f, -1.0f, 0.0f);

            Model horse = new Model("res/models/Horse/horse.gltf");
            for (int i = 0; i < horse.Models.Length; i++)
                horse.Models[i][0] = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(120.0f)) * Matrix4.CreateScale(25.0f) * Matrix4.CreateTranslation(-12.0f, -1.05f, -0.5f);

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
                    // "do it yourself" until opentk provides these enums
                    // https://github.com/opentk/opentk/issues/1450
                    const int SUBGROUP_SIZE_KHR = 0x9532;
                    const int SUBGROUP_SUPPORTED_FEATURES_KHR = 0x9534;
                    const int SUBGROUP_FEATURE_BASIC_BIT_KHR = 0x00000001;
                    const int SUBGROUP_FEATURE_ARITHMETIC_BIT_KHR = 0x00000004;

                    int bitfield = GL.GetInteger((GetPName)SUBGROUP_SUPPORTED_FEATURES_KHR);
                    
                    if ((bitfield & SUBGROUP_FEATURE_BASIC_BIT_KHR) == SUBGROUP_FEATURE_BASIC_BIT_KHR &&
                        (bitfield & SUBGROUP_FEATURE_ARITHMETIC_BIT_KHR) == SUBGROUP_FEATURE_ARITHMETIC_BIT_KHR)
                    {
                        effectiveSubGroupSize = GL.GetInteger((GetPName)SUBGROUP_SIZE_KHR);
                    }
                }
                srcCode = srcCode.Replace("__effectiveSubroupSize__", Convert.ToString(effectiveSubGroupSize));
                
                ForwardPassVRS = new VariableRateShading(new Shader(ShaderType.ComputeShader, srcCode), Size.X, Size.Y);
                VariableRateShading.BindVRSNV(ForwardPassVRS);
                VariableRateShading.SetShadingRatePaletteNV(shadingRates);
            }

            ForwardRenderer = new Forward(new Lighter(20, 20), Size.X, Size.Y, 6);
            Bloom = new Bloom(Size.X, Size.Y, 1.0f, 8.0f);
            SSR = new SSR(Size.X, Size.Y, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(Size.X, Size.Y, 14, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));
            SSAO = new SSAO(Size.X, Size.Y, 16, 0.25f, 2.0f);
            PostCombine = new PostCombine(Size.X, Size.Y);
            AtmosphericScatterer = new AtmosphericScatterer(256);
            AtmosphericScatterer.Compute();

            Bvh = new BVH(new BLAS(ModelSystem, 9));
            
            PathTracer = new PathTracer(Bvh, ModelSystem, AtmosphericScatterer.Result, Size.X, Size.Y);
            /// Driver bug: Global seamless cubemap feature may be ignored when sampling from uniform samplerCube
            /// in Compute Shader with ARB_bindless_texture activated. So try switching to seamless_cubemap_per_texture
            /// More info: https://stackoverflow.com/questions/68735879/opengl-using-bindless-textures-on-sampler2d-disables-texturecubemapseamless
            if (Helper.IsExtensionsAvailable("GL_AMD_seamless_cubemap_per_texture") || Helper.IsExtensionsAvailable("GL_ARB_seamless_cubemap_per_texture"))
                AtmosphericScatterer.Result.SetSeamlessCubeMapPerTextureARB_AMD(true);

            {
                List<GLSLLight> lights = new List<GLSLLight>(Lighter.GLSL_MAX_UBO_LIGHT_COUNT);
                lights.Add(new GLSLLight(new Vector3(-4.5f, 5.7f, -2.0f), new Vector3(3.5f, 0.8f, 0.9f) * 6.3f, 0.3f));
                lights.Add(new GLSLLight(new Vector3(-0.5f, 5.7f, -2.0f), new Vector3(0.5f, 3.8f, 0.9f) * 6.3f, 0.3f));
                lights.Add(new GLSLLight(new Vector3(4.5f, 5.7f, -2.0f), new Vector3(0.5f, 0.8f, 3.9f) * 6.3f, 0.3f));

                Random rng = new Random();
                Vector3 minPos = new Vector3(-17.0f, 0.0f, -16.0f);
                Vector3 maxPos = new Vector3( 17.0f, 20.0f, 16.0f);
                for (int i = 3; i < Lighter.GLSL_MAX_UBO_LIGHT_COUNT; i++)
                {
                    lights.Add(new GLSLLight(Helper.RandomVec3(minPos, maxPos), Helper.RandomVec3(1.0f, 4.0f), Helper.RandomFloat(0.05f, 0.15f)));
                }
                ForwardRenderer.LightingContext.Add(lights.ToArray());
            }
            
            pointShadows = new List<PointShadow>();
            pointShadows.Add(new PointShadow(ForwardRenderer.LightingContext, 0, 512, 0.5f, 60.0f));
            pointShadows.Add(new PointShadow(ForwardRenderer.LightingContext, 1, 512, 0.5f, 60.0f));
            pointShadows.Add(new PointShadow(ForwardRenderer.LightingContext, 2, 512, 0.5f, 60.0f));

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

            FinalProgram.Upload("IsDithering", IsDithering);

            gui.ImGuiController.WindowResized(Size.X, Size.Y);
            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), Size.X / (float)Size.Y, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;

            // I know this is bad practice but BVH polutes memory
            // and it doesn't seem to get cleaned up anytime soon without this
            GC.Collect();
        }


        protected override void OnResize()
        {
            gui.ImGuiController.WindowResized(Size.X, Size.Y);

            GLSLBasicData.Projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(102.0f), Size.X / (float)Size.Y, NEAR_PLANE, FAR_PLANE);
            GLSLBasicData.InvProjection = GLSLBasicData.Projection.Inverted();
            GLSLBasicData.NearPlane = NEAR_PLANE;
            GLSLBasicData.FarPlane = FAR_PLANE;
            ForwardRenderer.SetSize(Size.X, Size.Y);
            ForwardPassVRS.SetSize(Size.X, Size.Y);
            Bloom.SetSize(Size.X, Size.Y);
            VolumetricLight.SetSize(Size.X, Size.Y);
            SSR.SetSize(Size.X, Size.Y);
            SSAO.SetSize(Size.X, Size.Y);
            PostCombine.SetSize(Size.X, Size.Y);
            if (IsPathTracing)
            {
                PathTracer.SetSize(Size.X, Size.Y);
            }
            GLSLBasicData.FreezeFramesCounter = 0;
        }

        protected override void OnEnd()
        {

        }

        protected override void OnKeyPress(char key)
        {
            gui.ImGuiController.PressChar(key);
        }
    }
}
