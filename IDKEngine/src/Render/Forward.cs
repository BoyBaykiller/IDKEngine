using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class Forward
    {
        public readonly Texture Result;
        public readonly Texture NormalSpec;
        public readonly Texture MeshIndex;
        public readonly Texture Depth;
        public readonly Texture Velocity;

        private static readonly ShaderProgram shadingProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/fragment.glsl")));

        private static readonly ShaderProgram depthOnlyProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/DepthOnly/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/DepthOnly/fragment.glsl")));

        private static readonly ShaderProgram skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/SkyBox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/SkyBox/fragment.glsl")));

        public readonly Framebuffer Framebuffer;
        public bool IsZPrePass = true;
        public Forward(int width, int height)
        {
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba16f, (System.IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            NormalSpec = new Texture(TextureTarget2d.Texture2D);
            NormalSpec.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            NormalSpec.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba8, (System.IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            MeshIndex = new Texture(TextureTarget2d.Texture2D);
            MeshIndex.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            MeshIndex.MutableAllocate(width, height, 1, PixelInternalFormat.R32i, (System.IntPtr)0, PixelFormat.RedInteger, PixelType.Int);

            Velocity = new Texture(TextureTarget2d.Texture2D);
            Velocity.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Velocity.MutableAllocate(width, height, 1, PixelInternalFormat.Rg8, (System.IntPtr)0, PixelFormat.Rg, PixelType.Float);

            Depth = new Texture(TextureTarget2d.Texture2D);
            Depth.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Depth.MutableAllocate(width, height, 1, PixelInternalFormat.DepthComponent24, (System.IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);

            Framebuffer = new Framebuffer();
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, Result);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, NormalSpec);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, MeshIndex);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment3, Velocity);
            Framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Depth);

            Framebuffer.SetReadBuffer(ReadBufferMode.ColorAttachment2);
            Framebuffer.SetDrawBuffers(new DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3 });
        }

        public void Render(ModelSystem modelSystem, Texture skyBox = null, Texture ambientOcclusion = null)
        {
            // TODO: Find better way to clear. Maybe just render to mesh buffer when necassary so you can call regular GL.Clear
            Framebuffer.Bind();
            Framebuffer.ClearBuffer(ClearBuffer.Color, 0, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 1, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 2, -1);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 3, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);

            if (ambientOcclusion != null)
                ambientOcclusion.BindToUnit(0);
            else
                Texture.UnbindFromUnit(0);

            if (modelSystem.Meshes.Length > 0)
            {
                if (IsZPrePass)
                {
                    GL.ColorMask(false, false, false, false);

                    depthOnlyProgram.Use();
                    modelSystem.Draw();

                    GL.DepthFunc(DepthFunction.Equal);
                    GL.ColorMask(true, true, true, true);
                    GL.DepthMask(false);
                }
                
                shadingProgram.Use();
                modelSystem.Draw();
            }

            if (skyBox != null)
            {
                skyBox.BindToUnit(0);

                GL.DepthMask(false);
                GL.Disable(EnableCap.CullFace);
                GL.DepthFunc(DepthFunction.Lequal);

                skyBoxProgram.Use();
                GL.DrawArrays(PrimitiveType.Quads, 0, 24);

                GL.Enable(EnableCap.CullFace);
            }
            
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Less);
        }

        public void SetSize(int width, int height)
        {
            Result.MutableAllocate(width, height, 1, Result.PixelInternalFormat, (System.IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            Depth.MutableAllocate(width, height, 1, Depth.PixelInternalFormat, (System.IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);
            NormalSpec.MutableAllocate(width, height, 1, NormalSpec.PixelInternalFormat, (System.IntPtr)0, PixelFormat.Rgb, PixelType.Float);
            MeshIndex.MutableAllocate(width, height, 1, MeshIndex.PixelInternalFormat, (System.IntPtr)0, PixelFormat.RedInteger, PixelType.Int);
            Velocity.MutableAllocate(width, height, 1, PixelInternalFormat.Rg8, (System.IntPtr)0, PixelFormat.Rg, PixelType.Float);
        }
    }
}
