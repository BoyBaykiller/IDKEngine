using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadow
    {
        public const int GLSL_MAX_UBO_POINT_SHADOW_COUNT = 64; // also change UBO size in shaders

        public static readonly bool IS_VERTEX_LAYERED_RENDERING =
            (Helper.IsExtensionsAvailable("GL_ARB_shader_viewport_layer_array") ||
            Helper.IsExtensionsAvailable("GL_AMD_vertex_shader_layer") ||
            Helper.IsExtensionsAvailable("GL_NV_viewport_array") ||
            Helper.IsExtensionsAvailable("GL_NV_viewport_array2"));

        private static int _countPointShadows;
        public static int CountPointShadows
        {
            get => _countPointShadows;

            protected set
            {
                unsafe
                {
                    _countPointShadows = value;
                    shadowsBuffer.SubData(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow), sizeof(int), _countPointShadows);
                }
            }
        }

        private Vector3 _position;
        public unsafe Vector3 Position
        {
            get => _position;

            set
            {
                _position = value;

                glslPointShadow.PosX = Camera.GenerateMatrix(_position, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
                glslPointShadow.NegX = Camera.GenerateMatrix(_position, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
                glslPointShadow.PosY = Camera.GenerateMatrix(_position, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)) * projection;
                glslPointShadow.NegY = Camera.GenerateMatrix(_position, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)) * projection;
                glslPointShadow.PosZ = Camera.GenerateMatrix(_position, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
                glslPointShadow.NegZ = Camera.GenerateMatrix(_position, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;

                shadowsBuffer.SubData(Instance * sizeof(GLSLPointShadow), sizeof(GLSLPointShadow), glslPointShadow);
            }
        }

        private static readonly ShaderProgram renderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Shadows/PointShadows/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Shadows/PointShadows/fragment.glsl")));

        private static readonly ShaderProgram cullingProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/shadowCompute.glsl")));

        private static readonly BufferObject shadowsBuffer = InitShadowBuffer();
        public readonly Texture Result;
        public readonly int Instance;
        private readonly Matrix4 projection;

        private readonly Framebuffer framebuffer;
        private readonly Lighter lightContext;
        private GLSLPointShadow glslPointShadow;
        public PointShadow(Lighter lightContext, int lightIndex, int size, float nearPlane, float farPlane)
        {
            Debug.Assert(CountPointShadows + 1 <= GLSL_MAX_UBO_POINT_SHADOW_COUNT);

            Instance = CountPointShadows++;

            Result = new Texture(TextureTarget2d.TextureCubeMap);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.SetCompareMode(TextureCompareMode.CompareRefToTexture);
            Result.SetCompareFunc(All.Less);
            Result.ImmutableAllocate(size, size, 1, (SizedInternalFormat)PixelInternalFormat.DepthComponent16);

            framebuffer = new Framebuffer();
            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Result);
            framebuffer.SetDrawBuffers(new DrawBuffersEnum[] { DrawBuffersEnum.None });
            framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);

            projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90.0f), 1.0f, nearPlane, farPlane);

            glslPointShadow.Sampler = Result.MakeHandleResidentARB();
            glslPointShadow.NearPlane = nearPlane;
            glslPointShadow.FarPlane = farPlane;
            glslPointShadow.LightIndex = lightIndex;

            Position = lightContext.Lights[glslPointShadow.LightIndex].Position;

            this.lightContext = lightContext;
        }

        public unsafe void CreateDepthMap(ModelSystem modelSystem)
        {
            if (IS_VERTEX_LAYERED_RENDERING)
            {
                cullingProgram.Use();
                cullingProgram.Upload(0, Instance);
                GL.DispatchCompute((modelSystem.Meshes.Length + 12 - 1) / 12, 1, 1);
            }

            if (Position != lightContext.Lights[glslPointShadow.LightIndex].Position)
                Position = lightContext.Lights[glslPointShadow.LightIndex].Position;

            GL.Viewport(0, 0, Result.Width, Result.Height);
            framebuffer.Bind();
            framebuffer.Clear(ClearBufferMask.DepthBufferBit);

            renderProgram.Use();
            renderProgram.Upload(0, Instance);

            if (IS_VERTEX_LAYERED_RENDERING) // GL_ARB_shader_viewport_layer_array or GL_AMD_vertex_shader_layer or GL_NV_viewport_array or GL_NV_viewport_array2
            {
                GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
                modelSystem.Draw();
            }
            else
            {
                // Using geometry shader would be slower
                fixed (Matrix4* ptr = &glslPointShadow.PosX)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        Matrix4 projView = *(ptr + i);
                        modelSystem.ViewCull(ref projView);

                        framebuffer.SetTextureLayer(FramebufferAttachment.DepthAttachment, Result, i);

                        renderProgram.Upload(1, i);

                        modelSystem.Draw();
                    }
                }
                framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Result);
            }
        }

        private static unsafe BufferObject InitShadowBuffer()
        {
            BufferObject bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow) + sizeof(int), (System.IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferRange(BufferRangeTarget.UniformBuffer, 2, 0, bufferObject.Size);

            return bufferObject;
        }
    }
}
