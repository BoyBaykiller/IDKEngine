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

        public bool IsDepthPrePass = true;
        public bool IsExperimentalOcean = false;

        public Texture Result => isPing ? taaPing : taaPong;

        public readonly Framebuffer Framebuffer;
        public Texture NormalSpecTexture;
        public Texture VelocityTexture;
        public Texture DepthTexture;
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

        private Texture taaPing;
        private Texture taaPong;
        private bool isPing = true;
        public Forward(Lighter lighter, int width, int height, int taaSamples)
        {
            Debug.Assert(taaSamples <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);

            shadingProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Forward/Shading/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Forward/Shading/fragment.glsl")));

            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TAAResolve/compute.glsl")));

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

            TaaBuffer = new BufferObject();
            TaaBuffer.ImmutableAllocate(sizeof(GLSLTaaData), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            TaaBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

            taaData = Helper.Malloc<GLSLTaaData>();
            taaData->Samples = taaSamples;
            taaData->IsEnabled = 1;
            taaData->Scale = 5.0f;

            SetSize(width, height);

            TaaBuffer.SubData(0, sizeof(GLSLTaaData), (IntPtr)taaData);

            LightingContext = lighter;
        }

        public void Render(ModelSystem modelSystem, Texture ambientOcclusion = null)
        {
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, isPing ? taaPing : taaPong);
            Framebuffer.Bind();
            Framebuffer.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (ambientOcclusion != null)
                ambientOcclusion.BindToUnit(0);
            else
                Texture.UnbindFromUnit(0);

            if (IsDepthPrePass)
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


            GL.DepthMask(false);
            GL.Disable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Lequal);

            skyBoxProgram.Use();
            GL.DrawArrays(PrimitiveType.Quads, 0, 24);

            GL.DepthFunc(DepthFunction.Less);
            GL.Enable(EnableCap.CullFace);
            GL.DepthMask(true);

            LightingContext.Draw();

            if (taaData->IsEnabled == 1)
            {
                (isPing ? taaPing : taaPong).BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);
                (isPing ? taaPong : taaPing).BindToUnit(0);
                VelocityTexture.BindToUnit(1);
                DepthTexture.BindToUnit(2);
                taaResolveProgram.Use();
                GL.DispatchCompute((taaPing.Width + 8 - 1) / 8, (taaPing.Height + 8 - 1) / 8, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
            }

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
            if (taaPing != null) taaPing.Dispose();
            taaPing = new Texture(TextureTarget2d.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            if (taaPong != null) taaPong.Dispose();
            taaPong = new Texture(TextureTarget2d.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

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

            Span<float> jitterData = new Span<float>(taaData->Jitters, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT * 2);
            MyMath.GetHaltonSequence_2_3(jitterData);
            MyMath.MapHaltonSequence(jitterData, width, height);
            fixed (void* ptr = jitterData)
            {
                TaaBuffer.SubData(0, sizeof(float) * jitterData.Length, (IntPtr)ptr);
            }

            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment0, taaPing);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, NormalSpecTexture);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, VelocityTexture);
            Framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);

            Framebuffer.SetReadBuffer(ReadBufferMode.ColorAttachment2);
            Framebuffer.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment2});
        }

        public void Dispose()
        {
            DepthTexture.Dispose();
            VelocityTexture.Dispose();
            NormalSpecTexture.Dispose();
            taaPong.Dispose();
            taaPing.Dispose();

            Framebuffer.Dispose();

            TaaBuffer.Dispose();
            
            shadingProgram.Dispose();
            taaResolveProgram.Dispose();
            depthOnlyProgram.Dispose();
            skyBoxProgram.Dispose();
            aabbProgram.Dispose();

            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)taaData);
        }
    }
}
