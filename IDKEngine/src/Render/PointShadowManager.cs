using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadowManager : IDisposable
    {
        public const int GPU_MAX_UBO_POINT_SHADOW_COUNT = 16; // used in shader and client code - keep in sync!
        public static readonly bool IS_MESH_SHADER_RENDERING = false; // Helper.IsExtensionsAvailable("GL_NV_mesh_shader")

        private readonly PointShadow[] pointShadows;
        private readonly ShaderProgram renderProgram;
        private readonly ShaderProgram cullingProgram;
        private readonly BufferObject pointShadowsBuffer;
        public unsafe PointShadowManager()
        {
            pointShadows = new PointShadow[GPU_MAX_UBO_POINT_SHADOW_COUNT];

            if (IS_MESH_SHADER_RENDERING)
            {
                renderProgram = new ShaderProgram(
                    new Shader((ShaderType)NvMeshShader.TaskShaderNv, File.ReadAllText("res/shaders/Shadows/PointShadow/MeshPath/task.glsl")),
                    new Shader((ShaderType)NvMeshShader.MeshShaderNv, File.ReadAllText("res/shaders/Shadows/PointShadow/MeshPath/mesh.glsl")),
                    new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Shadows/PointShadow/fragment.glsl")));
            }
            else
            {
                renderProgram = new ShaderProgram(
                    new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Shadows/PointShadow/VertexPath/vertex.glsl")),
                    new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Shadows/PointShadow/fragment.glsl")));
            }


            cullingProgram = new ShaderProgram(
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/MeshCulling/PointShadow/Cull/compute.glsl")));

            pointShadowsBuffer = new BufferObject();
            pointShadowsBuffer.ImmutableAllocate(GPU_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GpuPointShadow) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            pointShadowsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 1);
        }

        public void RenderShadowMaps(ModelSystem modelSystem)
        {
            GL.ColorMask(false, false, false, false);
            GL.Disable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Less);
            for (int i = 0; i < GPU_MAX_UBO_POINT_SHADOW_COUNT; i++)
            {
                if (TryGetPointShadow(i, out PointShadow pointShadow))
                {
                    UploadPointShadow(i);

                    renderProgram.Upload(0, i);
                    cullingProgram.Upload(0, i);
                    pointShadow.Render(modelSystem, renderProgram, cullingProgram);
                }
            }
            GL.ColorMask(true, true, true, true);
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

        public void RemovePointShadow(int index)
        {
            if (!TryGetPointShadow(index, out _))
            {
                Logger.Log(Logger.LogLevel.Warn, $"Cannot remove {nameof(PointShadow)} {index} as it already is null");
                return;
            }

            pointShadows[index].Dispose();
            pointShadows[index] = null;
        }

        private unsafe void UploadPointShadow(int index)
        {
            if (!TryGetPointShadow(index, out PointShadow pointShadow))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(PointShadow)} {index} does not exist. Cannot update it's buffer content");
                return;
            }

            pointShadowsBuffer.SubData(index * sizeof(GpuPointShadow), sizeof(GpuPointShadow), pointShadow.GetGpuData());
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
                    RemovePointShadow(i);
                }
            }

            renderProgram.Dispose();
            cullingProgram.Dispose();
        }
    }
}
