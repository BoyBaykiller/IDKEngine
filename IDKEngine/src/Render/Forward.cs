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
        // this will be ceil(log2(EntityTypeValues))
        public const int ENTITY_BIFIELD_BITS_FOR_TYPE = 2; // used in shader and client code - keep in sync!
        
        /// <summary>
        /// Used to extract the entity type out of the entity indices textures. 16 matches bit depth of texture.
        /// </summary>
        public enum EntityType : uint
        {
            None  = 0u << (16 - ENTITY_BIFIELD_BITS_FOR_TYPE),
            Mesh  = 1u << (16 - ENTITY_BIFIELD_BITS_FOR_TYPE),
            Light = 2u << (16 - ENTITY_BIFIELD_BITS_FOR_TYPE),
        }


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

        public Texture Result => isPing ? taaPing : taaPong;

        public readonly Framebuffer Framebuffer;
        public readonly Texture NormalSpecTexture;
        public readonly Texture EntityIndicesTexture;
        public readonly Texture VelocityTexture;
        public readonly Texture DepthTexture;
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
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/TiledFordward/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/TiledFordward/fragment.glsl")));

            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TAAResolve/compute.glsl")));

            depthOnlyProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/TiledFordward/DepthOnly/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/TiledFordward/DepthOnly/fragment.glsl")));

            skyBoxProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/SkyBox/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/SkyBox/fragment.glsl")));

            aabbProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/AABB/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/AABB/fragment.glsl")));

            taaPing = new Texture(TextureTarget2d.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba16f, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            taaPong = new Texture(TextureTarget2d.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba16f, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            NormalSpecTexture = new Texture(TextureTarget2d.Texture2D);
            NormalSpecTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            NormalSpecTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            NormalSpecTexture.MutableAllocate(width, height, 1, PixelInternalFormat.Rgba8Snorm, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);

            EntityIndicesTexture = new Texture(TextureTarget2d.Texture2D);
            EntityIndicesTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            EntityIndicesTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            // if bitdepth changes also adjust enum "EntityClearColorMask"
            EntityIndicesTexture.MutableAllocate(width, height, 1, PixelInternalFormat.R16ui, (IntPtr)0, PixelFormat.RedInteger, PixelType.UnsignedInt);
            
            VelocityTexture = new Texture(TextureTarget2d.Texture2D);
            VelocityTexture.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            VelocityTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            VelocityTexture.MutableAllocate(width, height, 1, PixelInternalFormat.Rg16f, (IntPtr)0, PixelFormat.Rg, PixelType.Float);

            DepthTexture = new Texture(TextureTarget2d.Texture2D);
            DepthTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            DepthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            DepthTexture.MutableAllocate(width, height, 1, PixelInternalFormat.DepthComponent24, (IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);

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
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment1, NormalSpecTexture);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment2, EntityIndicesTexture);
            Framebuffer.SetRenderTarget(FramebufferAttachment.ColorAttachment3, VelocityTexture);
            Framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);

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
            Framebuffer.ClearBuffer(ClearBuffer.Color, 2, (uint)EntityType.None);
            Framebuffer.ClearBuffer(ClearBuffer.Color, 3, 0.0f);
            Framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);

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

            GL.DepthFunc(DepthFunction.Less);
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

        public EntityType ExtractEntityAndIndex(uint entityIndexBitfield, out uint index)
        {
            index = 0;

            if ((entityIndexBitfield & (uint)EntityType.Mesh) == (uint)EntityType.Mesh)
            {
                index = entityIndexBitfield & ((1u << (16 - ENTITY_BIFIELD_BITS_FOR_TYPE)) - 1);
                return EntityType.Mesh;
            }

            if ((entityIndexBitfield & (uint)EntityType.Light) == (uint)EntityType.Light)
            {
                index = entityIndexBitfield & ((1u << (16 - ENTITY_BIFIELD_BITS_FOR_TYPE)) - 1);
                return EntityType.Light;
            }

            return EntityType.None;
        }

        public void SetSize(int width, int height)
        {
            taaPing.MutableAllocate(width, height, 1, taaPing.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            taaPong.MutableAllocate(width, height, 1, taaPong.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            DepthTexture.MutableAllocate(width, height, 1, DepthTexture.PixelInternalFormat, (IntPtr)0, PixelFormat.DepthComponent, PixelType.Float);
            NormalSpecTexture.MutableAllocate(width, height, 1, NormalSpecTexture.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgb, PixelType.Float);
            EntityIndicesTexture.MutableAllocate(width, height, 1, EntityIndicesTexture.PixelInternalFormat, (IntPtr)0, PixelFormat.RedInteger, PixelType.Int);
            VelocityTexture.MutableAllocate(width, height, 1, VelocityTexture.PixelInternalFormat, (IntPtr)0, PixelFormat.Rg, PixelType.Float);

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
