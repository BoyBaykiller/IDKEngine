using System;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class CpuPointShadow : IDisposable
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

                if (TakeMeshShaderPath && !BBG.GetDeviceInfo().ExtensionSupport.MeshShader)
                {
                    Logger.Log(Logger.LogLevel.Error, $"Mesh shader path requires GL_NV_mesh_shader");
                    TakeMeshShaderPath = false;
                }

                if (renderShadowMapProgram != null) renderShadowMapProgram.Dispose();
                BBG.AbstractShaderProgram.SetShaderInsertionValue("TAKE_MESH_SHADER_PATH_SHADOW", TakeMeshShaderPath);
                if (TakeMeshShaderPath)
                {
                    renderShadowMapProgram = new BBG.AbstractShaderProgram(
                       new BBG.AbstractShader(BBG.ShaderStage.TaskNV, "Shadows/PointShadow/MeshPath/task.glsl"),
                       new BBG.AbstractShader(BBG.ShaderStage.MeshNV, "Shadows/PointShadow/MeshPath/mesh.glsl"),
                       new BBG.AbstractShader(BBG.ShaderStage.Fragment, "Shadows/PointShadow/fragment.glsl"));
                }
                else
                {
                    renderShadowMapProgram = new BBG.AbstractShaderProgram(
                        new BBG.AbstractShader(BBG.ShaderStage.Vertex, "Shadows/PointShadow/VertexPath/vertex.glsl"),
                        new BBG.AbstractShader(BBG.ShaderStage.Fragment, "Shadows/PointShadow/fragment.glsl"));
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

        public BBG.Texture ShadowMap;
        public BBG.Texture RayTracedShadowMap;

        private BBG.Sampler nearestSampler;
        private BBG.Sampler shadowSampler;
        private GpuPointShadow gpuPointShadow;

        private static bool isLazyInitialized = false;
        private static BBG.AbstractShaderProgram renderShadowMapProgram;
        private static BBG.AbstractShaderProgram cullingProgram;

        public CpuPointShadow(int shadowMapSize, Vector2i rayTracedShadowMapSize, Vector2 clippingPlanes)
        {
            if (!isLazyInitialized)
            {
                TakeMeshShaderPath = false;
                cullingProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderStage.Compute, "MeshCulling/PointShadow/compute.glsl"));
                isLazyInitialized = true;
            }

            ClippingPlanes = clippingPlanes;
            SetSizeShadowMap(shadowMapSize);
            SetSizeRayTracedShadowMap(rayTracedShadowMapSize);
        }

        public void RenderShadowMap(ModelManager modelManager, Camera camera, int gpuPointShadowIndex)
        {
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

            BBG.Computing.Compute("Cubemap shadow map culling", () =>
            {
                modelManager.ResetInstanceCounts();

                cullingProgram.Upload(0, gpuPointShadowIndex);
                cullingProgram.Upload(1, numVisibleFaces);
                cullingProgram.Upload(2, visibleFaces);

                BBG.Cmd.UseShaderProgram(cullingProgram);
                BBG.Computing.Dispatch((modelManager.MeshInstances.Length + 64 - 1) / 64, 1, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
            });

            BBG.Rendering.Render("Generate Shadow Cubemap", new BBG.Rendering.RenderAttachments()
            {
                DepthAttachment = new BBG.Rendering.DepthAttachment()
                {
                    Texture = ShadowMap,
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Clear,
                }
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                EnabledCapabilities = [BBG.Rendering.Capability.DepthTest]
            }, () =>
            {
                renderShadowMapProgram.Upload(0, gpuPointShadowIndex);
                BBG.Cmd.UseShaderProgram(renderShadowMapProgram);

                BBG.Rendering.InferViewportSize();
                if (TakeMeshShaderPath)
                {
                    modelManager.MeshShaderDrawNV();
                }
                else
                {
                    modelManager.Draw();
                }
            });
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

        public void SetConnectedLight(int lightIndex)
        {
            gpuPointShadow.LightIndex = lightIndex;
        }

        public void SetSizeShadowMap(int size)
        {
            if (shadowSampler != null) { shadowSampler.Dispose(); }
            if (nearestSampler != null) { nearestSampler.Dispose(); }
            if (ShadowMap != null) { ShadowMap.Dispose(); }

            shadowSampler = new BBG.Sampler(new BBG.Sampler.State()
            {
                MinFilter = BBG.Sampler.MinFilter.Linear,
                MagFilter = BBG.Sampler.MagFilter.Linear,

                CompareMode = BBG.Sampler.CompareMode.CompareRefToTexture,
                CompareFunc = BBG.Sampler.CompareFunc.Less,
            });

            nearestSampler = new BBG.Sampler(new BBG.Sampler.State()
            {
                MinFilter = BBG.Sampler.MinFilter.Nearest,
                MagFilter = BBG.Sampler.MagFilter.Nearest,
            });

            ShadowMap = new BBG.Texture(BBG.Texture.Type.Cubemap);
            ShadowMap.ImmutableAllocate(size, size, 1, BBG.Texture.InternalFormat.D16Unorm);

            // Using bindless textures for cubemaps causes sampling issues on radeonsi driver
            gpuPointShadow.Texture = ShadowMap.GetTextureHandleARB(nearestSampler);
            gpuPointShadow.ShadowTexture = ShadowMap.GetTextureHandleARB(shadowSampler);
        }

        public void SetSizeRayTracedShadowMap(Vector2i size)
        {
            /// We only create the ressources and not handle computation for <see cref="RayTracedShadowMap"/>
            /// as this can be done more efficiently in <see cref="PointShadowManager"/>

            if (RayTracedShadowMap != null) RayTracedShadowMap.Dispose();

            RayTracedShadowMap = new BBG.Texture(BBG.Texture.Type.Texture2D);
            RayTracedShadowMap.ImmutableAllocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R8Unorm);
            RayTracedShadowMap.SetFilter(BBG.Sampler.MinFilter.Nearest, BBG.Sampler.MagFilter.Nearest);

            gpuPointShadow.RayTracedShadowTexture = RayTracedShadowMap.GetImageHandleARB(RayTracedShadowMap.Format);
        }

        public void Dispose()
        {
            RayTracedShadowMap.Dispose();

            shadowSampler.Dispose();
            nearestSampler.Dispose();
            ShadowMap.Dispose();
        }
    }
}
