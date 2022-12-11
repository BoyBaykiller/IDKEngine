using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class RasterizerPipeline : IDisposable
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

        private readonly ShaderProgram depthOnlyProgram;
        private readonly ShaderProgram shadingProgram;
        private readonly ShaderProgram skyBoxProgram;
        public RasterizerPipeline(int width, int height)
        {
            SSAO = new SSAO(width, height, 10, 0.1f, 2.0f);
            SSR = new SSR(width, height, 30, 8, 50.0f);
            VolumetricLight = new VolumetricLighter(width, height, 7, 0.758f, 50.0f, 5.0f, new Vector3(0.025f));

            ShadingRateClassifier = new ShadingRateClassifier(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl")), width, height);
            Span<NvShadingRateImage> shadingRates = stackalloc NvShadingRateImage[]
            {
                NvShadingRateImage.ShadingRate1InvocationPerPixelNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X1PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer2X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X2PixelsNv,
                NvShadingRateImage.ShadingRate1InvocationPer4X4PixelsNv
            };
            ShadingRateClassifier.SetShadingRatePaletteNV(shadingRates);
            ShadingRateClassifier.BindVRSNV(ShadingRateClassifier);

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

            SetSize(width, height);
        }

        public void Render(ModelSystem modelSystem, ForwardRenderer renderer)
        {
            // Last frames SSAO
            if (IsSSAO)
                SSAO.Compute(renderer.DepthTexture, renderer.NormalSpecTexture);

            GL.Viewport(0, 0, renderer.Result.Width, renderer.Result.Height);

            if (IsWireframe)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            
            if (IsVariableRateShading)
                ShadingRateClassifier.IsEnabled = true;
            renderer.Render(depthOnlyProgram, shadingProgram, skyBoxProgram, modelSystem, IsSSAO ? SSAO.Result : null);
            ShadingRateClassifier.IsEnabled = false;
            
            if (IsWireframe)
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            if (IsVolumetricLighting)
                VolumetricLight.Compute(renderer.DepthTexture);

            if (IsSSR)
                SSR.Compute(renderer.Result, renderer.NormalSpecTexture, renderer.DepthTexture);
        }

        public void VariableRateShading(Texture image, Texture velocityTexture)
        {
            if (ShadingRateClassifier.HAS_VARIABLE_RATE_SHADING)
            {
                if (IsVariableRateShading)
                    ShadingRateClassifier.Compute(image, velocityTexture);
            }
            // Small "hack" to enable VRS debug image on systems that don't support the extension
            else if (ShadingRateClassifier.DebugValue != ShadingRateClassifier.DebugMode.NoDebug)
            {
                ShadingRateClassifier.Compute(image, velocityTexture);
            }
        }

        public void SetSize(int width, int height)
        {
            SSAO.SetSize(width, height);
            SSR.SetSize(width, height);
            VolumetricLight.SetSize(width, height);
            ShadingRateClassifier.SetSize(width, height);
        }

        public void Dispose()
        {
            if (SSAO != null) SSAO.Dispose();
            if (SSR != null) SSR.Dispose();
            if (VolumetricLight != null) SSR.Dispose();
            if (ShadingRateClassifier != null) ShadingRateClassifier.Dispose();
        }
    }
}
