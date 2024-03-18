using System;
using System.IO;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class PointShadowManager : IDisposable
    {
        public const int GPU_MAX_UBO_POINT_SHADOW_COUNT = 128; // used in shader and client code - keep in sync!
        public static readonly bool TAKE_MESH_SHADER_PATH = false; // Helper.IsExtensionsAvailable("GL_NV_mesh_shader")

        private readonly PointShadow[] pointShadows;
        private readonly ShaderProgram renderProgram;
        private readonly ShaderProgram cullingProgram;
        private readonly TypedBuffer<GpuPointShadow> pointShadowsBuffer;
        public unsafe PointShadowManager()
        {
            pointShadows = new PointShadow[GPU_MAX_UBO_POINT_SHADOW_COUNT];

            if (TAKE_MESH_SHADER_PATH)
            {
                renderProgram = new ShaderProgram(
                    Shader.ShaderFromFile((ShaderType)NvMeshShader.TaskShaderNv, "Shadows/PointShadow/MeshPath/task.glsl"),
                    Shader.ShaderFromFile((ShaderType)NvMeshShader.MeshShaderNv, "Shadows/PointShadow/MeshPath/mesh.glsl"),
                    Shader.ShaderFromFile(ShaderType.FragmentShader, "Shadows/PointShadow/fragment.glsl"));
            }
            else
            {
                renderProgram = new ShaderProgram(
                    Shader.ShaderFromFile(ShaderType.VertexShader, "Shadows/PointShadow/VertexPath/vertex.glsl"),
                    Shader.ShaderFromFile(ShaderType.FragmentShader, "Shadows/PointShadow/fragment.glsl"));
            }


            Dictionary<string, string> cullingShaderInsertions = new Dictionary<string, string>();
            cullingShaderInsertions.Add(nameof(TAKE_MESH_SHADER_PATH), TAKE_MESH_SHADER_PATH ? "1" : "0");
            cullingProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "MeshCulling/PointShadow/compute.glsl", cullingShaderInsertions));

            pointShadowsBuffer = new TypedBuffer<GpuPointShadow>();
            pointShadowsBuffer.ImmutableAllocate(BufferObject.BufferStorageType.Dynamic, GPU_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GpuPointShadow) + sizeof(int));
            pointShadowsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 1);
        }

        public void RenderShadowMaps(ModelSystem modelSystem, Camera camera)
        {
            GL.Disable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Less);
            for (int i = 0; i < GPU_MAX_UBO_POINT_SHADOW_COUNT; i++)
            {
                if (TryGetPointShadow(i, out PointShadow pointShadow))
                {
                    UploadPointShadow(i);

                    // tell shaders which point shadow we are rendering
                    renderProgram.Upload(0, i);
                    cullingProgram.Upload(0, i);

                    pointShadow.Render(modelSystem, renderProgram, cullingProgram, camera);
                }
            }
        }

        public bool TryAddPointShadow(PointShadow pointShadow, out int index)
        {
            index = -1;
            for (int i = 0; i < pointShadows.Length; i++)
            {
                ref PointShadow it = ref pointShadows[i];
                if (it == null)
                {
                    it = pointShadow;
                    UploadPointShadow(i);
                    
                    index = i;
                    return true;
                }
            }

            Logger.Log(Logger.LogLevel.Warn, $"Cannot add {nameof(PointShadow)}. Limit of {GPU_MAX_UBO_POINT_SHADOW_COUNT} is reached");
            return false;
        }

        public void DeletePointShadow(int index)
        {
            if (!TryGetPointShadow(index, out _))
            {
                Logger.Log(Logger.LogLevel.Warn, $"Cannot delete {nameof(PointShadow)} {index} as it already is null");
                return;
            }

            pointShadows[index].Dispose();
            pointShadows[index] = null;
        }

        private void UploadPointShadow(int index)
        {
            if (!TryGetPointShadow(index, out PointShadow pointShadow))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(PointShadow)} {index} does not exist. Cannot update it's buffer content");
                return;
            }

            pointShadowsBuffer.UploadElements(pointShadow.GetGpuData(), index);
        }

        public bool TryGetPointShadow(int index, out PointShadow pointShadow)
        {
            pointShadow = null;
            if (index < 0 || index >= GPU_MAX_UBO_POINT_SHADOW_COUNT) return false;

            pointShadow = pointShadows[index];
            return pointShadow != null;
        }

        public void Dispose()
        {
            pointShadowsBuffer.Dispose();
            for (int i = 0; i < pointShadows.Length; i++)
            {
                if (pointShadows[i] != null)
                {
                    DeletePointShadow(i);
                }
            }

            renderProgram.Dispose();
            cullingProgram.Dispose();
        }
    }
}
