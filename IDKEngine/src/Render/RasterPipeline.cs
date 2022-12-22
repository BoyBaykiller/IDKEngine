using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class RasterPipeline : IDisposable
    {
        public bool IsWireframe;
        public bool IsSSAO;
        public bool IsSSR;
        public bool IsVolumetricLighting;
        public bool IsVariableRateShading;

        public readonly SSAO SSAO;
        public readonly SSR SSR;
        public readonly VolumetricLighter VolumetricLight;
        public readonly ShadingRateClassifier ShadingRateClassifier;
        public readonly Voxelizer Voxelizer;

        public Texture Result;
        public Texture NormalSpecTexture;
        public Texture VelocityTexture;
        public Texture DepthTexture;

        private readonly ShaderProgram depthOnlyProgram;
        private readonly ShaderProgram shadingProgram;
        private readonly ShaderProgram skyBoxProgram;
        private readonly Framebuffer framebuffer;
        public RasterPipeline(int width, int height)
        {
            NvShadingRateImage[] shadingRates = new NvShadingRateImage[]
            {
                NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
            };
            ShadingRateClassifier = new ShadingRateClassifier(shadingRates, new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl")), width, height);
            SSAO = new SSAO(width, height, 10, 0.1f, 2.0f);
            SSR = new SSR(width, height, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(width, height, 7, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));
            Voxelizer = new Voxelizer(384, 384, 384, new Vector3(-28.0f, -3.0f, -17.0f), new Vector3(28.0f, 20.0f, 17.0f));

            depthOnlyProgram = new ShaderProgram(
                    new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Forward/DepthOnly/vertex.glsl")),
                    new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Forward/DepthOnly/fragment.glsl")));

            shadingProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Forward/Shading/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Forward/Shading/fragment.glsl")));

            skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/SkyBox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/SkyBox/fragment.glsl")));

            IsWireframe = false;
            IsSSAO = true;
            IsSSR = false;
            IsVolumetricLighting = true;
            IsVariableRateShading = false;

            framebuffer = new Framebuffer();

            SetSize(width, height);
        }

        public void Voxelize(ModelSystem modelSystem)
        {
            Voxelizer.Render(modelSystem);
            //Voxelizer.DebugRender(PostProcessor.Result);
        }

        public void Render(ModelSystem modelSystem, ForwardRenderer renderer)
        {
            // Last frames SSAO
            if (IsSSAO)
                SSAO.Compute(DepthTexture, NormalSpecTexture);

            GL.Viewport(0, 0, Result.Width, Result.Height);

            if (IsWireframe)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

            framebuffer.Bind();
            framebuffer.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            if (IsVariableRateShading)
            {
                ShadingRateClassifier.IsEnabled = true;
            }
            SSAO.Result.BindToUnit(0);
            Voxelizer.ResultVoxelAlbedo.BindToUnit(1);

            renderer.Draw(modelSystem, depthOnlyProgram, shadingProgram, skyBoxProgram, IsSSAO ? SSAO.Result : null);
            ShadingRateClassifier.IsEnabled = false;
            
            if (IsWireframe)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            if (IsVolumetricLighting)
                VolumetricLight.Compute(DepthTexture);

            if (IsSSR)
                SSR.Compute(Result, NormalSpecTexture, DepthTexture);
        }

        public void ComputeShadingRateImage(Texture src)
        {
            if (ShadingRateClassifier.HAS_VARIABLE_RATE_SHADING)
            {
                if (IsVariableRateShading)
                {
                    ShadingRateClassifier.Compute(src, VelocityTexture);
                }
            }
            // Small "hack" to enable VRS debug image on systems that don't support the extension
            else if (ShadingRateClassifier.DebugValue != ShadingRateClassifier.DebugMode.NoDebug)
            {
                ShadingRateClassifier.Compute(src, VelocityTexture);
            }
        }

        public void SetSize(int width, int height)
        {
            SSAO.SetSize(width, height);
            SSR.SetSize(width, height);
            VolumetricLight.SetSize(width, height);
            ShadingRateClassifier.SetSize(width, height);

            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            if (NormalSpecTexture != null) NormalSpecTexture.Dispose();
            NormalSpecTexture = new Texture(TextureTarget2d.Texture2D);
            NormalSpecTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            NormalSpecTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            NormalSpecTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba8Snorm);

            if (VelocityTexture != null) VelocityTexture.Dispose();
            VelocityTexture = new Texture(TextureTarget2d.Texture2D);
            VelocityTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            VelocityTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            VelocityTexture.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rg16f);

            if (DepthTexture != null) DepthTexture.Dispose();
            DepthTexture = new Texture(TextureTarget2d.Texture2D);
            DepthTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            DepthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            DepthTexture.ImmutableAllocate(width, height, 1, (SizedInternalFormat)PixelInternalFormat.DepthComponent24);

            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, Result);
            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, NormalSpecTexture);
            framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, VelocityTexture);
            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);

            framebuffer.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2 });
        }

        public void Dispose()
        {
            framebuffer.Dispose();
            SSAO.Dispose();
            SSR.Dispose();
            VolumetricLight.Dispose();
            ShadingRateClassifier.Dispose();
            Voxelizer.Dispose();
        }
    }
}
