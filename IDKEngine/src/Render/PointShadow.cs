using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadow : IDisposable
    {
        public static readonly bool TAKE_VERTEX_LAYERED_RENDERING_PATH =
            (Helper.IsExtensionsAvailable("GL_ARB_shader_viewport_layer_array") ||
            Helper.IsExtensionsAvailable("GL_NV_viewport_array2") ||
            Helper.IsExtensionsAvailable("GL_AMD_vertex_shader_layer"));

        public unsafe Vector3 Position
        {
            get => glslPointShadow.Position;

            set
            {
                glslPointShadow.Position = value;

                UpdateViewMatrices();
            }
        }

        public Vector2 ClippingPlanes
        {
            get => new Vector2(glslPointShadow.NearPlane, glslPointShadow.FarPlane);

            set
            {
                glslPointShadow.NearPlane = value.X;
                glslPointShadow.FarPlane = value.Y;

                glslPointShadow.NearPlane = MathF.Max(glslPointShadow.NearPlane, 0.1f);
                glslPointShadow.FarPlane = MathF.Max(glslPointShadow.FarPlane, 0.1f);

                glslPointShadow.NearPlane = MathF.Min(glslPointShadow.NearPlane, glslPointShadow.FarPlane - 0.01f);
                glslPointShadow.FarPlane = MathF.Max(glslPointShadow.FarPlane, glslPointShadow.NearPlane + 0.01f);

                projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90.0f), 1.0f, glslPointShadow.NearPlane, glslPointShadow.FarPlane);
            }
        }

        private Matrix4 _projection;
        private Matrix4 projection
        {
            get => _projection;

            set
            {
                _projection = value;
                UpdateViewMatrices();
            }
        }

        public Texture Result;
        
        private readonly Framebuffer framebuffer;
        private SamplerObject shadowSampler;
        private GLSLPointShadow glslPointShadow;
        public PointShadow(int size, float nearPlane, float farPlane)
        {
            framebuffer = new Framebuffer();
            framebuffer.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.None });

            ClippingPlanes = new Vector2(nearPlane, farPlane);
            SetSize(size);
        }

        public unsafe void Render(ModelSystem modelSystem, int pointShadowIndex, ShaderProgram renderProgram, ShaderProgram cullingProgram)
        {
            if (TAKE_VERTEX_LAYERED_RENDERING_PATH)
            {
                cullingProgram.Use();
                cullingProgram.Upload(0, pointShadowIndex);
                GL.DispatchCompute((modelSystem.Meshes.Length + 64 - 1) / 64, 1, 1);
            }

            GL.Viewport(0, 0, Result.Width, Result.Height);
            framebuffer.Bind();
            framebuffer.Clear(ClearBufferMask.DepthBufferBit);

            renderProgram.Upload(0, pointShadowIndex);

            if (TAKE_VERTEX_LAYERED_RENDERING_PATH) // GL_ARB_shader_viewport_layer_array or GL_NV_viewport_array2 or GL_AMD_vertex_shader_layer
            {
                GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
                renderProgram.Use();
                modelSystem.Draw();
            }
            else
            {
                // Using geometry shader would be slower
                for (int i = 0; i < 6; i++)
                {
                    ref readonly Matrix4 projView = ref glslPointShadow[GLSLPointShadow.Matrix.PosX + i];
                    modelSystem.FrustumCull(projView);

                    framebuffer.SetRenderTargetLayer(FramebufferAttachment.DepthAttachment, Result, i);

                    renderProgram.Use();
                    renderProgram.Upload(1, i);

                    modelSystem.Draw();
                }
                framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Result);
            }
        }

        private void UpdateViewMatrices()
        {
            glslPointShadow.PosX = Camera.GenerateViewMatrix(glslPointShadow.Position, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
            glslPointShadow.NegX = Camera.GenerateViewMatrix(glslPointShadow.Position, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
            glslPointShadow.PosY = Camera.GenerateViewMatrix(glslPointShadow.Position, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)) * projection;
            glslPointShadow.NegY = Camera.GenerateViewMatrix(glslPointShadow.Position, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)) * projection;
            glslPointShadow.PosZ = Camera.GenerateViewMatrix(glslPointShadow.Position, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
            glslPointShadow.NegZ = Camera.GenerateViewMatrix(glslPointShadow.Position, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
        }

        public ref readonly GLSLPointShadow GetGLSLData()
        {
            return ref glslPointShadow;
        }

        public void SetSize(int size)
        {
            size = Math.Max(size, 1);

            DisposeBindlessTextures();

            shadowSampler = new SamplerObject();
            shadowSampler.SetSamplerParamter(SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            shadowSampler.SetSamplerParamter(SamplerParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            shadowSampler.SetSamplerParamter(SamplerParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            shadowSampler.SetSamplerParamter(SamplerParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            shadowSampler.SetSamplerParamter(SamplerParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
            shadowSampler.SetSamplerParamter(SamplerParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
            shadowSampler.SetSamplerParamter(SamplerParameterName.TextureCompareFunc, (int)All.Less);

            Result = new Texture(TextureTarget2d.TextureCubeMap);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(size, size, 1, (SizedInternalFormat)PixelInternalFormat.DepthComponent16);

            glslPointShadow.Texture = Result.GetTextureHandleARB();
            glslPointShadow.ShadowTexture = Result.GetTextureHandleARB(shadowSampler);

            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Result);
            framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);
        }

        private void DisposeBindlessTextures()
        {
            if (shadowSampler != null) { shadowSampler.Dispose(); }
            if (Result != null) { Result.Dispose(); }
        }

        public void Dispose()
        {
            framebuffer.Dispose();
            DisposeBindlessTextures();
        }
    }
}
