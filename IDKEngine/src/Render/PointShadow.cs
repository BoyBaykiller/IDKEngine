using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class PointShadow : IDisposable
    {
        public Vector3 Position
        {
            get => gpuPointShadow.Position;

            set
            {
                gpuPointShadow.Position = value;

                UpdateViewMatrices();
            }
        }

        public Vector2 ClippingPlanes
        {
            get => new Vector2(gpuPointShadow.NearPlane, gpuPointShadow.FarPlane);

            set
            {
                gpuPointShadow.NearPlane = value.X;
                gpuPointShadow.FarPlane = value.Y;

                gpuPointShadow.NearPlane = MathF.Max(gpuPointShadow.NearPlane, 0.1f);
                gpuPointShadow.FarPlane = MathF.Max(gpuPointShadow.FarPlane, 0.1f);

                gpuPointShadow.NearPlane = MathF.Min(gpuPointShadow.NearPlane, gpuPointShadow.FarPlane - 0.001f);
                gpuPointShadow.FarPlane = MathF.Max(gpuPointShadow.FarPlane, gpuPointShadow.NearPlane + 0.001f);

                projection = MyMath.CreatePerspectiveFieldOfViewDepthZeroToOne(MathHelper.DegreesToRadians(90.0f), 1.0f, gpuPointShadow.NearPlane, gpuPointShadow.FarPlane);
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
        private SamplerObject nearestSampler;
        private SamplerObject shadowSampler;
        private GpuPointShadow gpuPointShadow;
        public PointShadow(int size, Vector2 clippingPlanes)
        {
            framebuffer = new Framebuffer();
            framebuffer.SetDrawBuffers([DrawBuffersEnum.None]);

            ClippingPlanes = clippingPlanes;
            SetSize(size);
        }

        public void Render(ModelSystem modelSystem, ShaderProgram renderProgram, ShaderProgram cullingProgram, Camera camera)
        {
            GL.Viewport(0, 0, Result.Width, Result.Height);
            framebuffer.Bind();
            framebuffer.Clear(ClearBufferMask.DepthBufferBit);

            Matrix4 cameraProjView = camera.GetViewMatrix() * camera.GetProjectionMatrix();
            Frustum cameraFrustum = new Frustum(cameraProjView);
            Span<Vector3> cameraFrustumVertices = stackalloc Vector3[8];
            MyMath.GetFrustumPoints(Matrix4.Invert(cameraProjView), cameraFrustumVertices);

            int numVisibleFaces = 0;
            uint visibleFaces = 0;
            for (uint i = 0; i < 6; i++)
            {
                // We don't need to render a shadow face if it doesn't collide with the cameras frustum

                Matrix4 faceMatrix = gpuPointShadow[GpuPointShadow.RenderMatrix.PosX + (int)i];
                Frustum shadowFaceFrustum = new Frustum(faceMatrix);
                Span<Vector3> shadowFrustumVertices = stackalloc Vector3[8];
                MyMath.GetFrustumPoints(Matrix4.Invert(faceMatrix), shadowFrustumVertices);
                bool frustaIntersect = Intersections.ConvexSATIntersect(cameraFrustum, shadowFaceFrustum, cameraFrustumVertices, shadowFrustumVertices);

                if (frustaIntersect)
                {
                    visibleFaces |= (i << numVisibleFaces * 3);
                    numVisibleFaces++;
                }
            }
            cullingProgram.Upload(1, numVisibleFaces);
            cullingProgram.Upload(2, visibleFaces);

            modelSystem.ResetInstancesBeforeCulling();
            cullingProgram.Use();
            GL.DispatchCompute((modelSystem.MeshInstances.Length + 64 - 1) / 64, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.CommandBarrierBit);

            renderProgram.Use();
            if (PointShadowManager.TAKE_MESH_SHADER_PATH)
            {
                modelSystem.MeshShaderDrawNV();
            }
            else
            {
                modelSystem.Draw();
            }
        }

        private void UpdateViewMatrices()
        {
            gpuPointShadow.PosX = Camera.GenerateViewMatrix(gpuPointShadow.Position, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
            gpuPointShadow.NegX = Camera.GenerateViewMatrix(gpuPointShadow.Position, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
            gpuPointShadow.PosY = Camera.GenerateViewMatrix(gpuPointShadow.Position, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)) * projection;
            gpuPointShadow.NegY = Camera.GenerateViewMatrix(gpuPointShadow.Position, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)) * projection;
            gpuPointShadow.PosZ = Camera.GenerateViewMatrix(gpuPointShadow.Position, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
            gpuPointShadow.NegZ = Camera.GenerateViewMatrix(gpuPointShadow.Position, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)) * projection;
        }

        public ref readonly GpuPointShadow GetGpuData()
        {
            return ref gpuPointShadow;
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

            nearestSampler = new SamplerObject();
            nearestSampler.SetSamplerParamter(SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            nearestSampler.SetSamplerParamter(SamplerParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            nearestSampler.SetSamplerParamter(SamplerParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            nearestSampler.SetSamplerParamter(SamplerParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            nearestSampler.SetSamplerParamter(SamplerParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            Result = new Texture(TextureTarget2d.TextureCubeMap);
            Result.ImmutableAllocate(size, size, 1, SizedInternalFormat.DepthComponent16);

            gpuPointShadow.Texture = Result.GetTextureHandleARB(nearestSampler);
            gpuPointShadow.ShadowTexture = Result.GetTextureHandleARB(shadowSampler);

            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, Result);
            framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);
        }

        private void DisposeBindlessTextures()
        {
            if (shadowSampler != null) { shadowSampler.Dispose(); }
            if (nearestSampler != null) { nearestSampler.Dispose(); }
            if (Result != null) { Result.Dispose(); }
        }

        public void Dispose()
        {
            framebuffer.Dispose();
            DisposeBindlessTextures();
        }
    }
}
