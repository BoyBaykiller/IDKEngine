using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.OpenGL;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render
{
    class PathTracer : IDisposable
    {
        private int cachedRayDepth;
        
        public int _rayDepth;
        public int RayDepth
        {
            get => _rayDepth;
            set
            {
                _rayDepth = value;
                ResetRenderProcess();
            }
        }

        private uint _accumulatedSamples;
        public uint AccumulatedSamples
        {
            get => _accumulatedSamples;

            private set
            {
                _accumulatedSamples = value;
                wavefrontPTBuffer.UploadData(Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.AccumulatedSamples)), sizeof(uint), _accumulatedSamples);
            }
        }

        public bool IsDebugBVHTraversal
        {
            get => gpuSettings.IsDebugBVHTraversal == 1;

            set
            {
                gpuSettings.IsDebugBVHTraversal = value ? 1 : 0;

                if (value)
                {
                    cachedRayDepth = RayDepth;
                    RayDepth = 1;
                }
                else
                {
                    RayDepth = cachedRayDepth;
                }
                ResetRenderProcess();
            }
        }

        public bool IsTraceLights
        {
            get => gpuSettings.IsTraceLights == 1;

            set
            {
                gpuSettings.IsTraceLights = value ? 1 : 0;
                ResetRenderProcess();
            }
        }

        public float FocalLength
        {
            get => gpuSettings.FocalLength;

            set
            {
                gpuSettings.FocalLength = value;
                ResetRenderProcess();
            }
        }

        public float LenseRadius
        {
            get => gpuSettings.LenseRadius;

            set
            {
                gpuSettings.LenseRadius = value;
                ResetRenderProcess();
            }
        }
        public bool IsAlwaysTintWithAlbedo
        {
            get => gpuSettings.IsAlwaysTintWithAlbedo == 1;

            set
            {
                gpuSettings.IsAlwaysTintWithAlbedo = value ? 1 : 0;
                ResetRenderProcess();
            }
        }

        public struct GpuSettings
        {
            public float FocalLength;
            public float LenseRadius;
            public int IsDebugBVHTraversal;
            public int IsTraceLights;
            public int IsAlwaysTintWithAlbedo;

            public static GpuSettings Default = new GpuSettings()
            {
                FocalLength = 8.0f,
                LenseRadius = 0.01f,
                IsDebugBVHTraversal = 0,
                IsTraceLights = 0,
                IsAlwaysTintWithAlbedo = 0
            };
        }

        private GpuSettings gpuSettings;

        public Texture Result;
        private readonly AbstractShaderProgram firstHitProgram;
        private readonly AbstractShaderProgram nHitProgram;
        private readonly AbstractShaderProgram finalDrawProgram;
        private readonly TypedBuffer<GpuSettings> gpuSettingsBuffer;
        private TypedBuffer<GpuWavefrontRay> wavefrontRayBuffer;
        private BufferObject wavefrontPTBuffer;
        public PathTracer(Vector2i size, in GpuSettings settings)
        {
            firstHitProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "PathTracing/FirstHit/compute.glsl"));

            nHitProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "PathTracing/NHit/compute.glsl"));
            finalDrawProgram = new AbstractShaderProgram(new AbstractShader(ShaderType.ComputeShader, "PathTracing/FinalDraw/compute.glsl"));

            gpuSettingsBuffer = new TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            gpuSettings = settings;

            RayDepth = 7;
        }

        public void Compute()
        {
            gpuSettingsBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 7);
            gpuSettingsBuffer.UploadElements(gpuSettings);

            Result.BindToImageUnit(0, Result.TextureFormat);
            firstHitProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);

            wavefrontPTBuffer.Bind(BufferTarget.DispatchIndirectBuffer);
            nHitProgram.Use();
            for (int j = 1; j < RayDepth; j++)
            {
                int pingPongIndex = j % 2;

                nint pingPongIndexOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.PingPongIndex));
                wavefrontPTBuffer.UploadData(pingPongIndexOffset, sizeof(uint), pingPongIndex);

                nint rayCountsBaseOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.Counts));
                wavefrontPTBuffer.UploadData(rayCountsBaseOffset + (1 - pingPongIndex) * sizeof(uint), sizeof(uint), 0);

                GL.DispatchComputeIndirect(0);
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);
            }

            finalDrawProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);

            AccumulatedSamples++;
        }

        public unsafe void SetSize(Vector2i size)
        {
            float clear = 0.0f;

            if (Result != null) Result.Dispose();
            Result = new Texture(Texture.Type.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(size.X, size.Y, 1, Texture.InternalFormat.R32G32B32A32Float);
            Result.Clear(PixelFormat.Red, PixelType.Float, clear);

            if (wavefrontRayBuffer != null) wavefrontRayBuffer.Dispose();
            wavefrontRayBuffer = new TypedBuffer<GpuWavefrontRay>();
            wavefrontRayBuffer.ImmutableAllocateElements(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.None, size.X * size.Y);
            wavefrontRayBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7);

            if (wavefrontPTBuffer != null) wavefrontPTBuffer.Dispose();
            wavefrontPTBuffer = new BufferObject();
            wavefrontPTBuffer.ImmutableAllocate(BufferObject.MemLocation.DeviceLocal, BufferObject.MemAccess.Synced, sizeof(GpuWavefrontPTHeader) + (size.X * size.Y * sizeof(uint)));
            wavefrontPTBuffer.SimpleClear(0, sizeof(GpuWavefrontPTHeader), (nint)(&clear));
            wavefrontPTBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 8);

            ResetRenderProcess();
        }

        public void ResetRenderProcess()
        {
            AccumulatedSamples = 0;
        }

        public ref readonly GpuSettings GetGpuSettings()
        {
            return ref gpuSettings;
        }

        public void Dispose()
        {
            Result.Dispose();
            
            firstHitProgram.Dispose();
            nHitProgram.Dispose();
            finalDrawProgram.Dispose();

            gpuSettingsBuffer.Dispose();

            wavefrontRayBuffer.Dispose();
            wavefrontPTBuffer.Dispose();
        }
    }
}
