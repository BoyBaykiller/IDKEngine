using System;
using System.IO;
using IDKEngine.Render.Objects;
using OpenTK;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render
{
    class Forward
    {
        public const int MESH_INDEX_CLEAR_COLOR = -1;
        public const int GLSL_MAX_UBO_HALTON_SEQUENCE_COUNT = 256; // also change UBO size in shaders

        
        public bool IsDrawAABB = false;
        public readonly Framebuffer Framebuffer;
        public Texture Result => IsPing ? TaaPing : TaaPong;
        public readonly Texture NormalSpec;
        public readonly Texture MeshIndex;
        public readonly Texture Depth;
        public readonly Texture Velocity;
        public readonly Lighter LightingContext;
        
        private readonly Texture TaaPing;
        private readonly Texture TaaPong;
        private readonly BufferObject taaSettingsBuffer;
        private bool IsPing = false;

        private static readonly ShaderProgram shadingProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/fragment.glsl")));

        private static readonly ShaderProgram depthOnlyProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/DepthOnly/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/DepthOnly/fragment.glsl")));

        private static readonly ShaderProgram skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/SkyBox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/SkyBox/fragment.glsl")));

        private static readonly ShaderProgram aabbProgram = new ShaderProgram(
            new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/AABB/vertex.glsl")),
            new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/AABB/fragment.glsl")));

        private static readonly ShaderProgram taaResolveProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Fordward/taaResolveCompute.glsl")));

        public Forward(Lighter lighter, int width, int height)
        {
            TaaPing = new Texture(TextureTarget2d.Texture2D);
            TaaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            TaaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            TaaPing.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba16f, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            TaaPong = new Texture(TextureTarget2d.Texture2D);
            TaaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            TaaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            TaaPong.MutableAllocate(width, height, 1, TaaPing.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            NormalSpec = new Texture(TextureTarget2d.Texture2D);
            NormalSpec.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            NormalSpec.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba8Snorm, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            MeshIndex = new Texture(TextureTarget2d.Texture2D);
            MeshIndex.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            MeshIndex.MutableAllocate(width, height, 1, PixelInternalFormat.R32i, (IntPtr)0, PixelFormat.RedInteger, PixelType.Int);

            Velocity = new Texture(TextureTarget2d.Texture2D);
            Velocity.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Velocity.MutableAllocate(width, height, 1, PixelInternalFormat.Rg32f, (IntPtr)0, PixelFormat.Rg, PixelType.Float);

            Depth = new Texture(TextureTarget2d.Texture2D);
            Depth.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Depth.MutableAllocate(width, height, 1, PixelInternalFormat.DepthComponent24, (IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);

            Framebuffer = new Framebuffer();
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, TaaPing);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, NormalSpec);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, MeshIndex);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment3, Velocity);
            Framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Depth);

            Framebuffer.SetReadBuffer(ReadBufferMode.ColorAttachment2);
            Framebuffer.SetDrawBuffers(new DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3 });

            taaSettingsBuffer = new BufferObject();
            taaSettingsBuffer.ImmutableAllocate(Vector4.SizeInBytes * GLSL_MAX_UBO_HALTON_SEQUENCE_COUNT, MyMath.GetMapedHaltonSequence_2_3(GLSL_MAX_UBO_HALTON_SEQUENCE_COUNT, width, height), BufferStorageFlags.DynamicStorageBit);
            taaSettingsBuffer.BindBufferRange(BufferRangeTarget.UniformBuffer, 4, 0, taaSettingsBuffer.Size);

            LightingContext = lighter;
        }

        public void Render(ModelSystem modelSystem, Texture skyBox = null, Texture ambientOcclusion = null)
        {
            IsPing = !IsPing;

            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, IsPing ? TaaPing : TaaPong);

            Framebuffer.Bind();
            Framebuffer.ClearBuffer(ClearBuffer.Color, 0, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 1, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 2, MESH_INDEX_CLEAR_COLOR);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 3, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);

            if (ambientOcclusion != null)
                ambientOcclusion.BindToUnit(0);
            else
                Texture.UnbindFromUnit(0);

            if (modelSystem.Meshes.Length > 0)
            {
                GL.ColorMask(false, false, false, false);

                depthOnlyProgram.Use();
                modelSystem.Draw();

                GL.DepthFunc(DepthFunction.Equal);
                GL.ColorMask(true, true, true, true);
                GL.DepthMask(false);

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

            LightingContext.Draw();

            taaResolveProgram.Use();
            (IsPing ? TaaPing : TaaPong).BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);
            (IsPing ? TaaPong : TaaPing).BindToUnit(0);
            Velocity.BindToUnit(1);
            GL.DispatchCompute((TaaPing.Width + 8 - 1) / 8, (TaaPing.Height + 4 - 1) / 4, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            if (IsDrawAABB)
            {
                GL.Disable(EnableCap.CullFace);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

                aabbProgram.Use();
                GL.DrawArraysInstanced(PrimitiveType.Quads, 0, 24, modelSystem.Meshes.Length);

                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.Enable(EnableCap.CullFace);
            }
        }

        public void SetSize(int width, int height)
        {
            TaaPong.MutableAllocate(width, height, 1, TaaPong.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            TaaPing.MutableAllocate(width, height, 1, TaaPing.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            Depth.MutableAllocate(width, height, 1, Depth.PixelInternalFormat, (IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);
            NormalSpec.MutableAllocate(width, height, 1, NormalSpec.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgb, PixelType.Float);
            MeshIndex.MutableAllocate(width, height, 1, MeshIndex.PixelInternalFormat, (IntPtr)0, PixelFormat.RedInteger, PixelType.Int);
            Velocity.MutableAllocate(width, height, 1, Velocity.PixelInternalFormat, (IntPtr)0, PixelFormat.Rg, PixelType.Float);

            taaSettingsBuffer.SubData(0, taaSettingsBuffer.Size, MyMath.GetMapedHaltonSequence_2_3(GLSL_MAX_UBO_HALTON_SEQUENCE_COUNT, width, height));
        }
    }
}
