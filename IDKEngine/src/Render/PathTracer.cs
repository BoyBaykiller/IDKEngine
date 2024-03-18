using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
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

        private bool _isDebugBVHTraversal;
        public bool IsDebugBVHTraversal
        {
            get => _isDebugBVHTraversal;

            set
            {
                _isDebugBVHTraversal = value;
                finalDrawProgram.Upload("IsDebugBVHTraversal", _isDebugBVHTraversal);
                firstHitProgram.Upload("IsDebugBVHTraversal", _isDebugBVHTraversal);
                if (_isDebugBVHTraversal)
                {
                    firstHitProgram.Upload("ApertureDiameter", 0.0f);
                    cachedRayDepth = RayDepth;
                    RayDepth = 1;
                }
                else
                {
                    LenseRadius = _lenseRadius;
                    RayDepth = cachedRayDepth;
                }
                ResetRenderProcess();
            }
        }

        private bool _isTraceLights;
        public bool IsTraceLights
        {
            get => _isTraceLights;

            set
            {
                _isTraceLights = value;
                firstHitProgram.Upload("IsTraceLights", _isTraceLights);
                nHitProgram.Upload("IsTraceLights", _isTraceLights);
                ResetRenderProcess();
            }
        }

        private float _focalLength;
        public float FocalLength
        {
            get => _focalLength;

            set
            {
                _focalLength = value;
                firstHitProgram.Upload("FocalLength", value);
                ResetRenderProcess();
            }
        }

        private float _lenseRadius;
        public float LenseRadius
        {
            get => _lenseRadius;

            set
            {
                _lenseRadius = value;
                firstHitProgram.Upload("LenseRadius", value);
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

        private bool _onRefractionTintAlbedo;
        public bool IsOnRefractionTintAlbedo
        {
            get => _onRefractionTintAlbedo;

            set
            {
                _onRefractionTintAlbedo = value;
                firstHitProgram.Upload("IsOnRefractionTintAlbedo", IsOnRefractionTintAlbedo);
                nHitProgram.Upload("IsOnRefractionTintAlbedo", IsOnRefractionTintAlbedo);
                ResetRenderProcess();
            }
        }

        public Texture Result;
        private readonly ShaderProgram firstHitProgram;
        private readonly ShaderProgram nHitProgram;
        private readonly ShaderProgram finalDrawProgram;
        private TypedBuffer<GpuWavefrontRay> wavefrontRayBuffer;
        private BufferObject wavefrontPTBuffer;
        public PathTracer(int width, int height)
        {
            firstHitProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "PathTracing/FirstHit/compute.glsl"));
            nHitProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "PathTracing/NHit/compute.glsl"));
            finalDrawProgram = new ShaderProgram(Shader.ShaderFromFile(ShaderType.ComputeShader, "PathTracing/FinalDraw/compute.glsl"));

            SetSize(width, height);

            RayDepth = 7;
            FocalLength = 8.0f;
            LenseRadius = 0.01f;
        }

        public void Compute()
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, Result.SizedInternalFormat);
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

        public unsafe void SetSize(int width, int height)
        {
            float clear = 0.0f;

            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba32f);
            Result.Clear(PixelFormat.Red, PixelType.Float, clear);

            if (wavefrontRayBuffer != null) wavefrontRayBuffer.Dispose();
            wavefrontRayBuffer = new TypedBuffer<GpuWavefrontRay>();
            wavefrontRayBuffer.ImmutableAllocateElements(BufferObject.BufferStorageType.DeviceLocal, width * height);
            wavefrontRayBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 8);

            if (wavefrontPTBuffer != null) wavefrontPTBuffer.Dispose();
            wavefrontPTBuffer = new BufferObject();
            wavefrontPTBuffer.ImmutableAllocate(BufferObject.BufferStorageType.Dynamic, sizeof(GpuWavefrontPTHeader) + (width * height * sizeof(uint)));
            wavefrontPTBuffer.SimpleClear(0, sizeof(GpuWavefrontPTHeader), (nint)(&clear));
            wavefrontPTBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 9);

            ResetRenderProcess();
        }

        public void ResetRenderProcess()
        {
            AccumulatedSamples = 0;
        }

        public void Dispose()
        {
            Result.Dispose();
            
            firstHitProgram.Dispose();
            nHitProgram.Dispose();
            finalDrawProgram.Dispose();
            
            wavefrontRayBuffer.Dispose();
            wavefrontPTBuffer.Dispose();
        }
    }
}
