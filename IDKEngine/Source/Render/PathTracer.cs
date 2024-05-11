using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using BBOpenGL;
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

        public BBG.Texture Result;
        private readonly BBG.AbstractShaderProgram firstHitProgram;
        private readonly BBG.AbstractShaderProgram nHitProgram;
        private readonly BBG.AbstractShaderProgram finalDrawProgram;
        private readonly BBG.TypedBuffer<GpuSettings> gpuSettingsBuffer;
        private BBG.TypedBuffer<GpuWavefrontRay> wavefrontRayBuffer;
        private BBG.BufferObject wavefrontPTBuffer;
        public unsafe PathTracer(Vector2i size, in GpuSettings settings)
        {
            firstHitProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderType.Compute, "PathTracing/FirstHit/compute.glsl"));

            nHitProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderType.Compute, "PathTracing/NHit/compute.glsl"));
            finalDrawProgram = new BBG.AbstractShaderProgram(new BBG.AbstractShader(BBG.ShaderType.Compute, "PathTracing/FinalDraw/compute.glsl"));

            gpuSettingsBuffer = new BBG.TypedBuffer<GpuSettings>();
            gpuSettingsBuffer.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, 1);

            SetSize(size);

            gpuSettings = settings;

            RayDepth = 7;
        }

        public void Compute()
        {
            gpuSettingsBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.Uniform, 7);
            gpuSettingsBuffer.UploadElements(gpuSettings);

            BBG.Computing.Compute($"Trace Primary Rays", () =>
            {
                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.UseShaderProgram(firstHitProgram);
                BBG.Computing.Dispatch((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit | BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
            });

            for (int i = 1; i < RayDepth; i++)
            {
                BBG.Computing.Compute($"Trace Rays {i}", () =>
                {
                    int pingPongIndex = i % 2;

                    nint pingPongIndexOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.PingPongIndex));
                    wavefrontPTBuffer.UploadData(pingPongIndexOffset, sizeof(uint), pingPongIndex);

                    nint rayCountsBaseOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.Counts));
                    wavefrontPTBuffer.UploadData(rayCountsBaseOffset + (1 - pingPongIndex) * sizeof(uint), sizeof(uint), 0);

                    BBG.Cmd.UseShaderProgram(nHitProgram);
                    BBG.Computing.DispatchIndirect(wavefrontPTBuffer, 0);
                    BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit | BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
                });
            }

            BBG.Computing.Compute("Accumulate rays radiance on screen", () =>
            {
                BBG.Cmd.UseShaderProgram(finalDrawProgram);
                BBG.Computing.Dispatch((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit | BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit | BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
            });

            AccumulatedSamples++;
        }

        public unsafe void SetSize(Vector2i size)
        {
            float clear = 0.0f;

            if (Result != null) Result.Dispose();
            Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
            Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
            Result.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
            Result.ImmutableAllocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R32G32B32A32Float);
            Result.Clear(PixelFormat.Red, PixelType.Float, clear);

            if (wavefrontRayBuffer != null) wavefrontRayBuffer.Dispose();
            wavefrontRayBuffer = new BBG.TypedBuffer<GpuWavefrontRay>();
            wavefrontRayBuffer.ImmutableAllocateElements(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.None, size.X * size.Y);
            wavefrontRayBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 7);

            if (wavefrontPTBuffer != null) wavefrontPTBuffer.Dispose();
            wavefrontPTBuffer = new BBG.BufferObject();
            wavefrontPTBuffer.ImmutableAllocate(BBG.BufferObject.MemLocation.DeviceLocal, BBG.BufferObject.MemAccess.Synced, sizeof(GpuWavefrontPTHeader) + (size.X * size.Y * sizeof(uint)));
            wavefrontPTBuffer.SimpleClear(0, sizeof(GpuWavefrontPTHeader), &clear);
            wavefrontPTBuffer.BindBufferBase(BBG.BufferObject.BufferTarget.ShaderStorage, 8);

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
