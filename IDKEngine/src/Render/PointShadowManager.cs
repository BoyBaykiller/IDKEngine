using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;
using IDKEngine.OpenGL;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class PointShadowManager : IDisposable
    {
        public const int GPU_MAX_UBO_POINT_SHADOW_COUNT = 128; // used in shader and client code - keep in sync!

        private int _count;
        public int Count
        {
            private set
            {
                _count = value;
                pointShadowsBuffer.UploadData(pointShadowsBuffer.Size - sizeof(int), sizeof(int), Count);
            }

            get => _count;
        }

        private readonly PointShadow[] pointShadows;
        private readonly AbstractShaderProgram rayTracedShadowsProgram;
        private readonly TypedBuffer<GpuPointShadow> pointShadowsBuffer;
        public unsafe PointShadowManager()
        {
            pointShadows = new PointShadow[GPU_MAX_UBO_POINT_SHADOW_COUNT];

            rayTracedShadowsProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "ShadowsRayTraced/compute.glsl"));

            pointShadowsBuffer = new TypedBuffer<GpuPointShadow>();
            pointShadowsBuffer.ImmutableAllocate(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, GPU_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GpuPointShadow) + sizeof(int));
            pointShadowsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 2);
        }

        public void RenderShadowMaps(ModelSystem modelSystem, Camera camera)
        {
            GL.Disable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Less);
            for (int i = 0; i < Count; i++)
            {
                pointShadows[i].RenderShadowMap(modelSystem, camera, i);
            }
        }

        public void ComputeRayTracedShadowMaps(int samples)
        {
            if (Count == 0)
            {
                return;
            }

            Vector2i commonSize = new Vector2i();
            for (int i = 0; i < Count; i++)
            {
                PointShadow pointShadow = pointShadows[i];
                Vector2i size = new Vector2i(pointShadow.RayTracedShadowMap.Width, pointShadow.RayTracedShadowMap.Height);
                if (i == 0)
                {
                    commonSize = size;
                }
                if (commonSize != size)
                {
                    Logger.Log(Logger.LogLevel.Error, "Current implementation of ray traced shadows requires all ray traced shadow maps to be of the same size");
                    return;
                }
            }

            rayTracedShadowsProgram.Upload(0, samples);

            rayTracedShadowsProgram.Use();
            GL.DispatchCompute((commonSize.X + 8 - 1) / 8, (commonSize.Y + 8 - 1) / 8, Count);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }


        public bool AddPointShadow(PointShadow newPointShadow)
        {
            if (Count == GPU_MAX_UBO_POINT_SHADOW_COUNT)
            {
                Logger.Log(Logger.LogLevel.Warn, $"Cannot add {nameof(PointShadow)}. Limit of {GPU_MAX_UBO_POINT_SHADOW_COUNT} is reached");
                return false;
            }

            pointShadows[Count++] = newPointShadow;

            return true;
        }

        public void DeletePointShadow(int index)
        {
            if (!TryGetPointShadow(index, out PointShadow pointShadow))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(pointShadow)} {pointShadow} does not exist. Cannot delete it");
                return;
            }

            pointShadow.Dispose();

            if (Count > 0)
            {
                pointShadows[index] = pointShadows[--Count];
            }
        }

        public void Update()
        {
            for (int i = 0; i < Count; i++)
            {
                UploadPointShadow(i);
            }
        }

        private void UploadPointShadow(int index)
        {
            if (!TryGetPointShadow(index, out PointShadow pointShadow))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(PointShadow)} {index} does not exist. Cannot update it's buffer content");
                return;
            }

            pointShadowsBuffer.UploadElements(pointShadow.GetGpuPointShadow(), index);
        }

        public bool TryGetPointShadow(int index, out PointShadow pointShadow)
        {
            if (index >= 0 && index < Count)
            {
                pointShadow = pointShadows[index];
                return true;
            }

            pointShadow = null;
            return false;
        }

        public void SetSizeRayTracedShadows(Vector2i size)
        {
            for (int i = 0; i < Count; i++)
            {
                pointShadows[i].SetSizeRayTracedShadowMap(size);
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < pointShadows.Length; i++)
            {
                if (pointShadows[i] != null)
                {
                    DeletePointShadow(i);
                }
            }

            rayTracedShadowsProgram.Dispose();

            pointShadowsBuffer.Dispose();
        }
    }
}
