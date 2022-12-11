using System;
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

        private readonly BufferObject pointShadowsBuffer;
        private readonly PointShadow[] pointShadows;
        public unsafe PointShadowManager()
        {
            pointShadowsBuffer = new BufferObject();
            pointShadowsBuffer.ImmutableAllocate(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            pointShadowsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 1);

            pointShadows = new PointShadow[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
        }

        public void Add(PointShadow pointShadow)
        {
            Debug.Assert(Count < GLSL_MAX_UBO_POINT_SHADOW_COUNT);

            pointShadows[Count] = pointShadow;
            UploadPointShadow(Count);
            Count++;
        }

        public void UpdateShadowMaps(ModelSystem modelSystem)
        {
            GL.ColorMask(false, false, false, false);
            for (int i = 0; i < Count; i++)
            {
                PointShadow pointShadow = pointShadows[i];

                if (pointShadow.AttachedLightMoved())
                {
                    pointShadow.MoveToAttachedLight();
                    UploadPointShadow(i);
                }

                pointShadow.Render(modelSystem, i);
            }
        }

        private unsafe void UploadPointShadow(int index)
        {
            pointShadowsBuffer.SubData(index * sizeof(GLSLPointShadow), sizeof(GLSLPointShadow), pointShadows[index].GetGLSLData());
        }

        public void Dispose()
        {
            pointShadowsBuffer.Dispose();
            for (int i = 0; i < Count; i++)
            {
                pointShadows[i].Dispose();
            }
        }
    }
}
