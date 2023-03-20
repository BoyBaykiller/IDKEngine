using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadow : IDisposable
    {
        public static readonly bool HAS_VERTEX_LAYERED_RENDERING =
            (Helper.IsExtensionsAvailable("GL_ARB_shader_viewport_layer_array") ||
            Helper.IsExtensionsAvailable("GL_NV_viewport_array2") ||
            Helper.IsExtensionsAvailable("GL_AMD_vertex_shader_layer"));

        private Vector3 _position;
        public unsafe Vector3 Position
        {
            get => _position;

            set
            {
                _position = value;

                glslPointShadow.PosX = Camera.GenerateViewMatrix(_position, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
                glslPointShadow.NegX = Camera.GenerateViewMatrix(_position, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
                glslPointShadow.PosY = Camera.GenerateViewMatrix(_position, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)) * projection;
                glslPointShadow.NegY = Camera.GenerateViewMatrix(_position, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)) * projection;
                glslPointShadow.PosZ = Camera.GenerateViewMatrix(_position, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
                glslPointShadow.NegZ = Camera.GenerateViewMatrix(_position, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
                glslPointShadow.Position = _position;
            }
        }

        public Texture Result;
        private readonly Framebuffer framebuffer;
        private readonly SamplerObject shadowSampler;

        private GLSLPointShadow glslPointShadow;
        private readonly Matrix4 projection;
        public PointShadow(int size, float nearPlane, float farPlane)
        {
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

            framebuffer = new Framebuffer();
            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Result);
            framebuffer.SetDrawBuffers(stackalloc DrawBuffersEnum[] { DrawBuffersEnum.None });
            framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);

            projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90.0f), 1.0f, nearPlane, farPlane);

            glslPointShadow.Texture = Result.MakeTextureHandleARB();
            glslPointShadow.ShadowTexture = Result.MakeTextureSamplerHandleARB(shadowSampler);

            glslPointShadow.NearPlane = nearPlane;
            glslPointShadow.FarPlane = farPlane;
        }

        public unsafe void Render(ModelSystem modelSystem, int pointShadowIndex, ShaderProgram renderProgram, ShaderProgram cullingProgram)
        {
            if (HAS_VERTEX_LAYERED_RENDERING)
            {
                cullingProgram.Use();
                cullingProgram.Upload(0, pointShadowIndex);
                GL.DispatchCompute((modelSystem.Meshes.Length + 64 - 1) / 64, 1, 1);
            }

            GL.Viewport(0, 0, Result.Width, Result.Height);
            framebuffer.Bind();
            framebuffer.Clear(ClearBufferMask.DepthBufferBit);

            renderProgram.Upload(0, pointShadowIndex);

            if (HAS_VERTEX_LAYERED_RENDERING) // GL_ARB_shader_viewport_layer_array or GL_NV_viewport_array2 or GL_AMD_vertex_shader_layer
            {
                GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);
                renderProgram.Use();
                modelSystem.Draw();
            }
            else
            {
                fixed (Matrix4* ptr = &glslPointShadow.PosX)
                {
                    // Using geometry shader would be slower
                    for (int i = 0; i < 6; i++)
                    {
                        Matrix4 projView = *(ptr + i);
                        modelSystem.FrustumCull(projView);

                        framebuffer.SetRenderTargetLayer(FramebufferAttachment.DepthAttachment, Result, i);

                        renderProgram.Use();
                        renderProgram.Upload(1, i);

                        modelSystem.Draw();
                    }
                }
                framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Result);
            }
        }

        public ref readonly GLSLPointShadow GetGLSLData()
        {
            return ref glslPointShadow;
        }

        public void Dispose()
        {
            framebuffer.Dispose();

            // unmake texture handle resident for deletion
            Texture.UnmakeTextureHandleARB(glslPointShadow.Texture);
            Texture.UnmakeTextureHandleARB(glslPointShadow.ShadowTexture);

            Result.Dispose();
            shadowSampler.Dispose();
        }
    }
}
