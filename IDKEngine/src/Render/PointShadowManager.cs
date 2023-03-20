using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PointShadowManager : IDisposable
    {
        public const int GLSL_MAX_UBO_POINT_SHADOW_COUNT = 16; // used in shader and client code - keep in sync!
        
        private int _count;
        public int Count
        {
            get => _count;

            private set
            {
                unsafe
                {
                    _count = value;
                    pointShadowsBuffer.SubData(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow), sizeof(int), _count);
                }
            }
        }

        public readonly PointShadow[] PointShadows;

        private readonly BufferObject pointShadowsBuffer;
        private readonly ShaderProgram renderProgram;
        private readonly ShaderProgram cullingProgram;
        public unsafe PointShadowManager()
        {
            pointShadowsBuffer = new BufferObject();
            pointShadowsBuffer.ImmutableAllocate(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            pointShadowsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 1);

            PointShadows = new PointShadow[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
           
            renderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/Shadows/PointShadows/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/Shadows/PointShadows/fragment.glsl")));

            cullingProgram = new ShaderProgram(
                new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/Culling/Frustum/shadowCompute.glsl")));
        }

        public bool Add(PointShadow pointShadow)
        {
            if (Count == GLSL_MAX_UBO_POINT_SHADOW_COUNT)
            {
                return false;
            }

            PointShadows[Count++] = pointShadow;
            UploadPointShadow(Count - 1);

            return true;
        }

        public void RenderShadowMaps(ModelSystem modelSystem)
        {
            GL.ColorMask(false, false, false, false);
            GL.Disable(EnableCap.CullFace);
            for (int i = 0; i < Count; i++)
            {
                PointShadow pointShadow = PointShadows[i];

                pointShadow.Render(modelSystem, i, renderProgram, cullingProgram);
            }
            GL.Enable(EnableCap.CullFace);
            GL.ColorMask(true, true, true, true);
        }

        public unsafe void UploadPointShadow(int index)
        {
            pointShadowsBuffer.SubData(index * sizeof(GLSLPointShadow), sizeof(GLSLPointShadow), PointShadows[index].GetGLSLData());
        }

        public void Dispose()
        {
            pointShadowsBuffer.Dispose();
            for (int i = 0; i < Count; i++)
            {
                PointShadows[i].Dispose();
            }

            renderProgram.Dispose();
            cullingProgram.Dispose();
        }
    }
}
