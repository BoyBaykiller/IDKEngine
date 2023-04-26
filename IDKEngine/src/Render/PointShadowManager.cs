using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadowManager : IDisposable
    {
        public const int GLSL_MAX_UBO_POINT_SHADOW_COUNT = 16; // used in shader and client code - keep in sync!

        private readonly PointShadow[] pointShadows;
        private readonly ShaderProgram renderProgram;
        private readonly ShaderProgram cullingProgram;
        private readonly BufferObject pointShadowsBuffer;
        public unsafe PointShadowManager()
        {
            pointShadows = new PointShadow[GLSL_MAX_UBO_POINT_SHADOW_COUNT];

            renderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Shadows/PointShadows/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Shadows/PointShadows/fragment.glsl")));

            cullingProgram = new ShaderProgram(
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/Frustum/shadowCompute.glsl")));

            pointShadowsBuffer = new BufferObject();
            pointShadowsBuffer.ImmutableAllocate(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            pointShadowsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 1);
        }

        public void RenderShadowMaps(ModelSystem modelSystem)
        {
            GL.ColorMask(false, false, false, false);
            GL.Disable(EnableCap.CullFace);
            for (int i = 0; i < GLSL_MAX_UBO_POINT_SHADOW_COUNT; i++)
            {
                if (TryGetPointShadow(i, out PointShadow pointShadow))
                {
                    UploadPointShadow(i);
                    pointShadow.Render(modelSystem, i, renderProgram, cullingProgram);
                }
            }
            GL.Enable(EnableCap.CullFace);
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

            Logger.Log(Logger.LogLevel.Warn, $"Can not add {nameof(PointShadow)}. Limit of {GLSL_MAX_UBO_POINT_SHADOW_COUNT} is reached");
            return false;
        }

        public void RemovePointShadow(int index)
        {
            if (!TryGetPointShadow(index, out _))
            {
                Logger.Log(Logger.LogLevel.Warn, $"Can not remove {nameof(PointShadow)} {index} as it already is null");
                return;
            }

            pointShadows[index].Dispose();
            pointShadows[index] = null;
        }

        private unsafe void UploadPointShadow(int index)
        {
            if (!TryGetPointShadow(index, out PointShadow pointShadow))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(PointShadow)} {index} does not exist. Can not update it's buffer content");
                return;
            }

            pointShadowsBuffer.SubData(index * sizeof(GLSLPointShadow), sizeof(GLSLPointShadow), pointShadow.GetGLSLData());
        }

        public bool TryGetPointShadow(int index, out PointShadow pointShadow)
        {
            pointShadow = null;
            if (index < 0 || index >= GLSL_MAX_UBO_POINT_SHADOW_COUNT) return false;

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
