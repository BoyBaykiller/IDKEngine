using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    unsafe class ForwardRenderer : IDisposable
    {
        private int _renderMeshAABBIndex = -1;
        /// <summary>
        /// Any negative number will not render any AABB at all
        /// </summary>
        public int RenderMeshAABBIndex
        {
            get => _renderMeshAABBIndex;

            set
            {
                _renderMeshAABBIndex = value;
                aabbProgram.Upload(0, _renderMeshAABBIndex);
            }
        }

        public readonly Framebuffer Framebuffer;
        public Texture Result;
        public Texture NormalSpecTexture;
        public Texture VelocityTexture;
        public Texture DepthTexture;
        public readonly Lighter LightingContext;

        private readonly ShaderProgram shadingProgram;
        private readonly ShaderProgram depthOnlyProgram;
        private readonly ShaderProgram skyBoxProgram;
        private readonly ShaderProgram aabbProgram;

        public ForwardRenderer(Lighter lighter, int width, int height, int taaSamples)
        {
            Debug.Assert(taaSamples <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);

            shadingProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Forward/Shading/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Forward/Shading/fragment.glsl")));

            depthOnlyProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Forward/DepthOnly/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Forward/DepthOnly/fragment.glsl")));

            skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/SkyBox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/SkyBox/fragment.glsl")));

            aabbProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/AABB/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/AABB/fragment.glsl")));

            Framebuffer = new Framebuffer();

            SetSize(width, height);

            LightingContext = lighter;
        }

        public void Render(ModelSystem modelSystem, Texture ambientOcclusion = null)
        {
            Framebuffer.Bind();
            Framebuffer.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (ambientOcclusion != null)
                ambientOcclusion.BindToUnit(0);
            else
                Texture.UnbindFromUnit(0);

            GL.ColorMask(false, false, false, false);
            depthOnlyProgram.Use();
            modelSystem.Draw();

            GL.ColorMask(true, true, true, true);
            GL.DepthFunc(DepthFunction.Equal);
            GL.DepthMask(false);
            shadingProgram.Use();
            modelSystem.Draw();

            GL.Disable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Lequal);

            skyBoxProgram.Use();
            GL.DrawArrays(PrimitiveType.Quads, 0, 24);

            GL.Enable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Less);
            GL.DepthMask(true);

            LightingContext.Draw();

            if (RenderMeshAABBIndex >= 0)
            {
                GL.DepthFunc(DepthFunction.Always);
                GL.Disable(EnableCap.CullFace);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

                aabbProgram.Use();
                GL.DrawArrays(PrimitiveType.Quads, 0, 24);

                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.Enable(EnableCap.CullFace);
                GL.DepthFunc(DepthFunction.Less);
            }
        }

        public void SetSize(int width, int height)
        {
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

            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, Result);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, NormalSpecTexture);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, VelocityTexture);
            Framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);

            Framebuffer.SetReadBuffer(ReadBufferMode.ColorAttachment2);
            Framebuffer.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2 });
        }

        public void Dispose()
        {
            DepthTexture.Dispose();
            VelocityTexture.Dispose();
            NormalSpecTexture.Dispose();
            Result.Dispose();

            Framebuffer.Dispose();
            
            shadingProgram.Dispose();
            depthOnlyProgram.Dispose();
            skyBoxProgram.Dispose();
            aabbProgram.Dispose();
        }
    }
}
