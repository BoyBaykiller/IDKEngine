using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.OpenGL;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class PointShadow : IDisposable
    {
        private static bool _takeMeshShaderPath;
        public static bool TakeMeshShaderPath
        {
            get => _takeMeshShaderPath;

            set
            {
                if (_takeMeshShaderPath == value && renderShadowMapProgram != null)
                {
                    return;
                }
                _takeMeshShaderPath = value;

                if (TakeMeshShaderPath && !Helper.IsExtensionsAvailable("GL_NV_mesh_shader"))
                {
                    Logger.Log(Logger.LogLevel.Error, $"Mesh shader path requires GL_NV_mesh_shader");
                    TakeMeshShaderPath = false;
                }

                if (renderShadowMapProgram != null) renderShadowMapProgram.Dispose();
                AbstractShaderProgram.ShaderInsertions["TAKE_MESH_SHADER_PATH_SHADOW"] = TakeMeshShaderPath ? "1" : "0";
                if (TakeMeshShaderPath)
                {
                    renderShadowMapProgram = new AbstractShaderProgram(
                        new AbstractShader((ShaderType)NvMeshShader.TaskShaderNv, "Shadows/PointShadow/MeshPath/task.glsl"),
                        new AbstractShader((ShaderType)NvMeshShader.MeshShaderNv, "Shadows/PointShadow/MeshPath/mesh.glsl"),
                        new AbstractShader(ShaderType.FragmentShader, "Shadows/PointShadow/fragment.glsl"));
                }
                else
                {
                    renderShadowMapProgram = new AbstractShaderProgram(
                        new AbstractShader(ShaderType.VertexShader, "Shadows/PointShadow/VertexPath/vertex.glsl"),
                        new AbstractShader(ShaderType.FragmentShader, "Shadows/PointShadow/fragment.glsl"));
                }
            }
        }

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

        public Texture ShadowMap;
        public Texture RayTracedShadowMap;

        private readonly Framebuffer framebuffer;
        private Sampler nearestSampler;
        private Sampler shadowSampler;
        private GpuPointShadow gpuPointShadow;

        private static bool isLazyInitialized = false;
        private static AbstractShaderProgram renderShadowMapProgram;
        private static AbstractShaderProgram cullingProgram;

        public PointShadow(int shadowMapSize, Vector2i rayTracedShadowMapSize, Vector2 clippingPlanes)
        {
            if (!isLazyInitialized)
            {
                TakeMeshShaderPath = false;
                cullingProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "MeshCulling/PointShadow/compute.glsl"));
                isLazyInitialized = true;
            }

            framebuffer = new Framebuffer();
            framebuffer.SetDrawBuffers([DrawBuffersEnum.None]);

            ClippingPlanes = clippingPlanes;
            SetSizeShadowMap(shadowMapSize);
            SetSizeRayTracedShadowMap(rayTracedShadowMapSize);
        }

        public void RenderShadowMap(ModelSystem modelSystem, Camera camera, int gpuPointShadowIndex)
        {
            renderShadowMapProgram.Upload(0, gpuPointShadowIndex);
            cullingProgram.Upload(0, gpuPointShadowIndex);

            GL.Viewport(0, 0, ShadowMap.Width, ShadowMap.Height);
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

            renderShadowMapProgram.Use();
            if (TakeMeshShaderPath)
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

        public ref readonly GpuPointShadow GetGpuPointShadow()
        {
            return ref gpuPointShadow;
        }

        public void SetReferencingLightIndex(int lightIndex)
        {
            gpuPointShadow.LightIndex = lightIndex;
        }

        public void SetSizeShadowMap(int size)
        {
            size = Math.Max(size, 1);

            if (shadowSampler != null) { shadowSampler.Dispose(); }
            if (nearestSampler != null) { nearestSampler.Dispose(); }
            if (ShadowMap != null) { ShadowMap.Dispose(); }

            shadowSampler = new Sampler(new Sampler.State()
            {
                MinFilter = Sampler.MinFilter.Linear,
                MagFilter = Sampler.MagFilter.Linear,

                CompareMode = Sampler.CompareMode.CompareRefToTexture,
                CompareFunc = Sampler.CompareFunc.Less,
            });

            nearestSampler = new Sampler(new Sampler.State()
            {
                MinFilter = Sampler.MinFilter.Nearest,
                MagFilter = Sampler.MagFilter.Nearest,
            });

            ShadowMap = new Texture(Texture.Type.Cubemap);
            ShadowMap.ImmutableAllocate(size, size, 1, Texture.InternalFormat.D16Unorm);

            // Note: Using bindless textures for cubemaps causes sampling issues on radeonsi driver
            gpuPointShadow.Texture = ShadowMap.GetTextureHandleARB(nearestSampler);
            gpuPointShadow.ShadowTexture = ShadowMap.GetTextureHandleARB(shadowSampler);

            framebuffer.SetRenderTarget(FramebufferAttachment.DepthAttachment, ShadowMap);
            framebuffer.ClearBuffer(ClearBuffer.Depth, 0, 1.0f);
        }

        public void SetSizeRayTracedShadowMap(Vector2i size)
        {
            /// We only create the ressources and not handle computation for <see cref="RayTracedShadowMap"/>
            /// as this can be done more efficiently in <see cref="PointShadowManager"/>

            if (RayTracedShadowMap != null) RayTracedShadowMap.Dispose();

            RayTracedShadowMap = new Texture(Texture.Type.Texture2D);
            RayTracedShadowMap.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R8Unorm);
            RayTracedShadowMap.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            gpuPointShadow.RayTracedShadowTexture = RayTracedShadowMap.GetImageHandleARB(RayTracedShadowMap.TextureFormat);
        }

        public void Dispose()
        {
            framebuffer.Dispose();

            RayTracedShadowMap.Dispose();

            shadowSampler.Dispose();
            nearestSampler.Dispose();
            ShadowMap.Dispose();
        }
    }
}
