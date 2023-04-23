using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadowManager : IDisposable
    {
        public const int GLSL_MAX_UBO_POINT_SHADOW_COUNT = 16; // used in shader and client code - keep in sync!

        public readonly PointShadow[] PointShadows;

        private readonly BufferObject pointShadowsBuffer;
        private readonly ShaderProgram renderProgram;
        private readonly ShaderProgram cullingProgram;
        public unsafe PointShadowManager()
        {
            PointShadows = new PointShadow[GLSL_MAX_UBO_POINT_SHADOW_COUNT];

            renderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Shadows/PointShadows/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Shadows/PointShadows/fragment.glsl")));

            cullingProgram = new ShaderProgram(
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/Frustum/shadowCompute.glsl")));

            pointShadowsBuffer = new BufferObject();
            pointShadowsBuffer.ImmutableAllocate(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            pointShadowsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 1);
        }

        public bool TryAdd(PointShadow pointShadow, out int index)
        {
            index = -1;
            for (int i = 0; i < GLSL_MAX_UBO_POINT_SHADOW_COUNT; i++)
            {
                PointShadow it = PointShadows[i];
                if (it == null)
                {
                    PointShadows[i] = pointShadow;
                    UploadPointShadow(i);
                    
                    index = i;
                    return true;
                }
            }

            return false;
        }

        public void RemoveAt(int index)
        {
            if (PointShadows[index] == null)
            {
                Logger.Log(Logger.LogLevel.Warn, $"Can not remove PointShadow at index {index} as it already is null");
            }
            else
            {
                PointShadows[index].Dispose();
                PointShadows[index] = null;
            }
        }

        public void RenderShadowMaps(ModelSystem modelSystem)
        {
            GL.ColorMask(false, false, false, false);
            GL.Disable(EnableCap.CullFace);
            for (int i = 0; i < GLSL_MAX_UBO_POINT_SHADOW_COUNT; i++)
            {
                PointShadow pointShadow = PointShadows[i];
                if (pointShadow != null)
                {
                    UploadPointShadow(i);
                    pointShadow.Render(modelSystem, i, renderProgram, cullingProgram);
                }
            }
            GL.Enable(EnableCap.CullFace);
            GL.ColorMask(true, true, true, true);
        }

        private unsafe void UploadPointShadow(int index)
        {
            pointShadowsBuffer.SubData(index * sizeof(GLSLPointShadow), sizeof(GLSLPointShadow), PointShadows[index].GetGLSLData());
        }

        public void Dispose()
        {
            pointShadowsBuffer.Dispose();
            for (int i = 0; i < GLSL_MAX_UBO_POINT_SHADOW_COUNT; i++)
            {
                ref PointShadow pointShadow = ref PointShadows[i];
                if (pointShadow != null)
                {
                    pointShadow.Dispose();
                    pointShadow = null;
                }
            }

            renderProgram.Dispose();
            cullingProgram.Dispose();
        }
    }
}
