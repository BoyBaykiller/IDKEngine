using System.IO;
using System.Diagnostics;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadow : ShadowBase
    {
        public static readonly bool IS_VERTEX_LAYERED_RENDERING =
            (Helper.IsExtensionsAvailable("GL_ARB_shader_viewport_layer_array") ||
            Helper.IsExtensionsAvailable("GL_AMD_vertex_shader_layer") ||
            Helper.IsExtensionsAvailable("GL_NV_viewport_array") ||
            Helper.IsExtensionsAvailable("GL_NV_viewport_array2"));

        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Shadows/PointShadows/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Shadows/PointShadows/fragment.glsl"))
                );
        private readonly Matrix4 projection;
        private readonly Lighter lightContext;

        private GLSLPointShadow glslPointShadow;
        public readonly int Instance;
        public PointShadow(Lighter context, int lightIndex, int size, float nearPlane, float farPlane)
            : base(TextureTarget2d.TextureCubeMap)
        {
            Debug.Assert(CountPointShadows + 1 <= GLSL_MAX_UBO_POINT_SHADOW_COUNT);

            Instance = CountPointShadows++;

            DepthTexture.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            DepthTexture.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            DepthTexture.SetCompareMode(TextureCompareMode.CompareRefToTexture);
            DepthTexture.SetCompareFunc(All.Lequal);
            DepthTexture.ImmutableAllocate(size, size, 1, (SizedInternalFormat)PixelInternalFormat.DepthComponent16);
            
            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);
            framebuffer.SetDrawBuffers(new DrawBuffersEnum[] { DrawBuffersEnum.None });

            lightContext = context;
            projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90.0f), 1.0f, nearPlane, farPlane);

            glslPointShadow.Sampler = DepthTexture.MakeHandleResident();
            glslPointShadow.NearPlane = nearPlane;
            glslPointShadow.FarPlane = farPlane;
            glslPointShadow.LightIndex = lightIndex;

            lightContext = context;
            Position = context[glslPointShadow.LightIndex].Position;
        }

        private Vector3 _position;
        public Vector3 Position
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

                PointShadowUpload(Instance, glslPointShadow);
            }
        }

        public override void CreateDepthMap(ModelSystem modelSystem)
        {
            GL.Viewport(0, 0, DepthTexture.Width, DepthTexture.Height);
            GL.ColorMask(false, false, false, false);
            GL.CullFace(CullFaceMode.Front);
            framebuffer.Clear(ClearBufferMask.DepthBufferBit);

            shaderProgram.Use();
            shaderProgram.Upload(0, Instance);

            modelSystem.VAO.DisableVertexAttribute(1);
            modelSystem.VAO.DisableVertexAttribute(2);
            modelSystem.VAO.DisableVertexAttribute(3);
            modelSystem.VAO.DisableVertexAttribute(4);
            if (IS_VERTEX_LAYERED_RENDERING) // GL_ARB_shader_viewport_layer_array or GL_AMD_vertex_shader_layer or GL_NV_viewport_array or GL_NV_viewport_array2
            {
                modelSystem.ForEach(0, modelSystem.Meshes.Length, (ref Model.GLSLDrawCommand drawCommand) =>
                {
                    drawCommand.InstanceCount *= 6;
                });

                modelSystem.Draw();

                modelSystem.ForEach(0, modelSystem.Meshes.Length, (ref Model.GLSLDrawCommand drawCommand) =>
                {
                    drawCommand.InstanceCount /= 6;
                });
            }
            else
            {
                // Using geometry shader would be slower
                for (int i = 0; i < 6; i++)
                {
                    framebuffer.SetTextureLayer(FramebufferAttachment.DepthAttachment, DepthTexture, i);
                    shaderProgram.Upload(1, i);

                    modelSystem.Draw();
                }
                framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, DepthTexture);
            }
            modelSystem.VAO.EnableVertexAttribute(1);
            modelSystem.VAO.EnableVertexAttribute(2);
            modelSystem.VAO.EnableVertexAttribute(3);
            modelSystem.VAO.EnableVertexAttribute(4);

            GL.CullFace(CullFaceMode.Back);
            GL.ColorMask(true, true, true, true);
        }
    }
}
