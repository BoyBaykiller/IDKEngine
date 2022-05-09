using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    unsafe class Forward : IDisposable
    {
        public const int MESHES_CLEAR_COLOR = -1; // also change in shaders

        private int _renderMeshAABBIndex = -1;
        public int RenderMeshAABBIndex
        {
            get => _renderMeshAABBIndex;

            set
            {
                _renderMeshAABBIndex = value;
                aabbProgram.Upload(0, _renderMeshAABBIndex);
            }
        }

        public bool TaaEnabled
        {
            get => taaData->IsEnabled == 1 ? true : false;

            set
            {
                taaData->IsEnabled = value ? 1 : 0;
                TaaBuffer.SubData(Vector2.SizeInBytes * GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT + sizeof(int), sizeof(int), taaData->IsEnabled);
            }
        }
        public int TaaSamples
        {
            get => taaData->Samples;

            set
            {
                taaData->Samples = value;
                TaaBuffer.SubData(Vector2.SizeInBytes * GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT, sizeof(int), taaData->Samples);
            }
        }

        public Texture Result => isPing ? taaPing : taaPong;

        public readonly Framebuffer Framebuffer;
        public readonly Texture NormalSpec;
        public readonly Texture Meshes;
        public readonly Texture Velocity;
        public readonly Texture Depth;
        public readonly BufferObject TaaBuffer;
        public readonly Lighter LightingContext;

        private readonly ShaderProgram shadingProgram;
        private readonly ShaderProgram taaResolveProgram;
        private readonly ShaderProgram depthOnlyProgram;
        private readonly ShaderProgram skyBoxProgram;
        private readonly ShaderProgram aabbProgram;

        private int taaFrame
        {
            get => taaData->Frame;

            set
            {
                taaData->Frame = value;
                TaaBuffer.SubData(Vector2.SizeInBytes * GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT + 2 * sizeof(int), sizeof(int), taaData->Frame);
            }
        }

        private readonly GLSLTaaData* taaData;

        private readonly Texture taaPing;
        private readonly Texture taaPong;
        private bool isPing = true;

        public Forward(Lighter lighter, int width, int height, int taaSamples)
        {
            Debug.Assert(taaSamples <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);

            shadingProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/fragment.glsl")));

            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Fordward/TAAResolve/compute.glsl")));

            depthOnlyProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/DepthOnly/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/DepthOnly/fragment.glsl")));

            skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Fordward/SkyBox/compute.glsl")));

            aabbProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Fordward/AABB/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Fordward/AABB/fragment.glsl")));

            taaPing = new Texture(TextureTarget2d.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba16f, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            taaPong = new Texture(TextureTarget2d.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba16f, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            NormalSpec = new Texture(TextureTarget2d.Texture2D);
            NormalSpec.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            NormalSpec.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            NormalSpec.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba8Snorm, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            Meshes = new Texture(TextureTarget2d.Texture2D);
            Meshes.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Meshes.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Meshes.MutableAllocate(width, height, 1, PixelInternalFormat.R16i, (IntPtr)0, PixelFormat.RedInteger, PixelType.Int);

            Velocity = new Texture(TextureTarget2d.Texture2D);
            Velocity.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Velocity.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Velocity.MutableAllocate(width, height, 1, PixelInternalFormat.Rg16f, (IntPtr)0, PixelFormat.Rg, PixelType.Float);

            Depth = new Texture(TextureTarget2d.Texture2D);
            Depth.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Depth.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Depth.MutableAllocate(width, height, 1, PixelInternalFormat.DepthComponent24, (IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);

            taaData = Helper.Malloc<GLSLTaaData>();
            taaData->Samples = taaSamples;
            taaData->IsEnabled = 1;
            taaData->Scale = 5.0f;
            Span<float> jitterData = new Span<float>(taaData->Jitter, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT * 2);
            MyMath.GetHaltonSequence_2_3(jitterData);
            MyMath.MapHaltonSequence(jitterData, width, height);

            TaaBuffer = new BufferObject();
            TaaBuffer.ImmutableAllocate(sizeof(GLSLTaaData), (IntPtr)taaData, BufferStorageFlags.DynamicStorageBit);
            TaaBuffer.BindBufferRange(BufferRangeTarget.UniformBuffer, 5, 0, TaaBuffer.Size);

            Framebuffer = new Framebuffer();
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, taaPing);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, NormalSpec);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, Meshes);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment3, Velocity);
            Framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Depth);

            Framebuffer.SetReadBuffer(ReadBufferMode.ColorAttachment2);
            Framebuffer.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3 });

            LightingContext = lighter;
        }

        public void Render(ModelSystem modelSystem, Texture skyBox = null, Texture ambientOcclusion = null)
        {
            Framebuffer.Bind();
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, isPing ? taaPing : taaPong);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 0, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 1, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 2, MESHES_CLEAR_COLOR);
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

                GL.DepthMask(true);
                GL.DepthFunc(DepthFunction.Less);
            }

            LightingContext.Draw();

            if (taaData->IsEnabled == 1)
            {
                (isPing ? taaPing : taaPong).BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);
                (isPing ? taaPong : taaPing).BindToUnit(0);
                Velocity.BindToUnit(1);
                Depth.BindToUnit(2);
                taaResolveProgram.Use();
                GL.DispatchCompute((taaPing.Width + 8 - 1) / 8, (taaPing.Height + 8 - 1) / 8, 1);
            }

            if (skyBox != null)
            {
                Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
                Depth.BindToUnit(0);
                skyBox.BindToUnit(1);
                skyBoxProgram.Use();
                GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            }
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

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

            isPing = !isPing;
            taaFrame++;
        }

        public void SetSize(int width, int height)
        {
            taaPing.MutableAllocate(width, height, 1, taaPing.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            taaPong.MutableAllocate(width, height, 1, taaPong.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            Depth.MutableAllocate(width, height, 1, Depth.PixelInternalFormat, (IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);
            NormalSpec.MutableAllocate(width, height, 1, NormalSpec.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgb, PixelType.Float);
            Meshes.MutableAllocate(width, height, 1, Meshes.PixelInternalFormat, (IntPtr)0, PixelFormat.RedInteger, PixelType.Int);
            Velocity.MutableAllocate(width, height, 1, Velocity.PixelInternalFormat, (IntPtr)0, PixelFormat.Rg, PixelType.Float);

            Span<float> jitterData = new Span<float>(taaData->Jitter, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT * 2);
            MyMath.GetHaltonSequence_2_3(jitterData);
            MyMath.MapHaltonSequence(jitterData, width, height);
            fixed (void* ptr = jitterData)
            {
                TaaBuffer.SubData(0, sizeof(float) * jitterData.Length, (IntPtr)ptr);
            }
        }

        public void Dispose()
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)taaData);
        }
    }
}
