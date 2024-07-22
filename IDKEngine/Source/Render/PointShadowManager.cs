using System;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class PointShadowManager : IDisposable
    {
        public const int GPU_MAX_UBO_POINT_SHADOW_COUNT = 128; // Keep in sync between shader and client code!

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

        private readonly CpuPointShadow[] pointShadows;
        private readonly BBG.AbstractShaderProgram rayTracedShadowsProgram;
        private readonly BBG.TypedBuffer<GpuPointShadow> pointShadowsBuffer;
        public unsafe PointShadowManager()
        {
            pointShadows = new CpuPointShadow[GPU_MAX_UBO_POINT_SHADOW_COUNT];

            rayTracedShadowsProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "ShadowsRayTraced/compute.glsl"));

            pointShadowsBuffer = new BBG.TypedBuffer<GpuPointShadow>();
            pointShadowsBuffer.ImmutableAllocate(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.Synced, GPU_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GpuPointShadow) + sizeof(int));
            pointShadowsBuffer.BindBufferBase(BBG.Buffer.BufferTarget.Uniform, 2);
        }

        public void RenderShadowMaps(ModelManager modelManager, Camera camera)
        {
            for (int i = 0; i < Count; i++)
            {
                pointShadows[i].RenderShadowMap(modelManager, camera, i);
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
                CpuPointShadow pointShadow = pointShadows[i];
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

            BBG.Computing.Compute("Generate ray traced point light shadow maps", () =>
            {
                rayTracedShadowsProgram.Upload(0, samples);

                BBG.Cmd.UseShaderProgram(rayTracedShadowsProgram);
                BBG.Computing.Dispatch((commonSize.X + 8 - 1) / 8, (commonSize.Y + 8 - 1) / 8, Count);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderImageAccessBarrierBit);
            });
        }

        public bool TryAddPointShadow(CpuPointShadow newPointShadow, out int newIndex)
        {
            newIndex = -1;
            if (Count == GPU_MAX_UBO_POINT_SHADOW_COUNT)
            {
                Logger.Log(Logger.LogLevel.Warn, $"Cannot add {nameof(CpuPointShadow)}. Limit of {GPU_MAX_UBO_POINT_SHADOW_COUNT} is reached");
                return false;
            }

            newIndex = Count;
            pointShadows[Count++] = newPointShadow;

            return true;
        }

        public void DeletePointShadow(int index)
        {
            if (!TryGetPointShadow(index, out CpuPointShadow pointShadow))
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

        public void UpdateBuffer()
        {
            for (int i = 0; i < Count; i++)
            {
                UploadPointShadow(i);
            }
        }

        private unsafe void UploadPointShadow(int index)
        {
            if (!TryGetPointShadow(index, out CpuPointShadow pointShadow))
            {
                Logger.Log(Logger.LogLevel.Warn, $"{nameof(CpuPointShadow)} {index} does not exist. Cannot update it's buffer content");
                return;
            }

            pointShadowsBuffer.UploadElements(pointShadow.GetGpuPointShadow(), index);
        }

        public bool TryGetPointShadow(int index, out CpuPointShadow pointShadow)
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
