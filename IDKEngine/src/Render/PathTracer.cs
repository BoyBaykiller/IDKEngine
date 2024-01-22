using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
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
        public unsafe uint AccumulatedSamples
        {
            get => _accumulatedSamples;

            private set
            {
                _accumulatedSamples = value;
                wavefrontPTBuffer.SubData(Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.AccumulatedSamples)), sizeof(uint), _accumulatedSamples);
            }
        }

        public Texture Result;
        private readonly ShaderProgram firstHitProgram;
        private readonly ShaderProgram nHitProgram;
        private readonly ShaderProgram finalDrawProgram;
        private BufferObject wavefrontRayBuffer;
        private BufferObject wavefrontPTBuffer;
        public unsafe PathTracer(int width, int height)
        {
            firstHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/FirstHit/compute.glsl")));
            nHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/NHit/compute.glsl")));
            finalDrawProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/FinalDraw/compute.glsl")));

            SetSize(width, height);

            RayDepth = 7;
            FocalLength = 8.0f;
            LenseRadius = 0.01f;
        }

        public unsafe void Compute()
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, Result.SizedInternalFormat);
            firstHitProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);

            wavefrontPTBuffer.Bind(BufferTarget.DispatchIndirectBuffer);
            nHitProgram.Use();
            for (int j = 1; j < RayDepth; j++)
            {
                int pingPongIndex = 1 - (j % 2);
                nHitProgram.Upload(0, pingPongIndex);
                
                nint rayCountsBaseOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.Counts));
                wavefrontPTBuffer.SubData(rayCountsBaseOffset + pingPongIndex * sizeof(uint), sizeof(uint), 0);

                nint dispatchCmdsBaseOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.DispatchCommands));
                GL.DispatchComputeIndirect(dispatchCmdsBaseOffset + sizeof(GpuDispatchCmd) * (1 - pingPongIndex));
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);
            }

            finalDrawProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);

            AccumulatedSamples++;
        }

        public unsafe void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba32f);

            if (wavefrontRayBuffer != null) wavefrontRayBuffer.Dispose();
            wavefrontRayBuffer = new BufferObject();
            wavefrontRayBuffer.ImmutableAllocate(width * height * sizeof(GpuWavefrontRay), IntPtr.Zero, BufferStorageFlags.None);
            wavefrontRayBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 8);

            if (wavefrontPTBuffer != null) wavefrontPTBuffer.Dispose();
            wavefrontPTBuffer = new BufferObject();
            wavefrontPTBuffer.ImmutableAllocate(sizeof(GpuWavefrontPTHeader) + (width * height * sizeof(uint)), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            wavefrontPTBuffer.SubData(Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.DispatchCommands)), sizeof(GpuDispatchCmd) * 2, new GpuDispatchCmd[2]); // clear header dispatch cmds
            wavefrontPTBuffer.SubData(Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.Counts)), sizeof(Vector2i), new Vector2(0)); // clear header counts
            wavefrontPTBuffer.SubData(Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.AccumulatedSamples)), sizeof(uint), 0); // clear header counts
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
