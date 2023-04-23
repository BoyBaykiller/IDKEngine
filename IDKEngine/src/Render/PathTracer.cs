using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

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
                ResetRender();
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
                ResetRender();
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
                ResetRender();
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
                ResetRender();
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
                ResetRender();
            }
        }

        private uint _accumulatedSamples;
        public uint AccumulatedSamples
        {
            get => _accumulatedSamples;

            set
            {
                _accumulatedSamples = value;
                rayIndicesBuffer.SubData(2 * sizeof(uint), sizeof(uint), _accumulatedSamples);
            }
        }

        public Texture Result;
        private readonly ShaderProgram firstHitProgram;
        private readonly ShaderProgram nHitProgram;
        private readonly ShaderProgram finalDrawProgram;
        private readonly BufferObject dispatchCommandBuffer;
        private BufferObject transportRayBuffer;
        private BufferObject rayIndicesBuffer;
        public unsafe PathTracer(BVH bvh, int width, int height)
        {
            (string, string) appInsertions = ("MAX_BLAS_TREE_DEPTH", $"{Math.Max(bvh.MaxBlasTreeDepth, 1)}");
            
            firstHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/FirstHit/compute.glsl"),
                appInsertions
            ));

            nHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/NHit/compute.glsl"),
                appInsertions
            ));

            finalDrawProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/FinalDraw/compute.glsl")));

            dispatchCommandBuffer = new BufferObject();
            dispatchCommandBuffer.ImmutableAllocate(2 * sizeof(GLSLDispatchCommand), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            dispatchCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 8);

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

            dispatchCommandBuffer.Bind(BufferTarget.DispatchIndirectBuffer);
            nHitProgram.Use();
            for (int j = 1; j < RayDepth; j++)
            {
                int pingPongIndex = 1 - (j % 2);
                nHitProgram.Upload(0, pingPongIndex);
                rayIndicesBuffer.SubData(pingPongIndex * sizeof(uint), sizeof(uint), 0u); // reset ray counter

                GL.DispatchComputeIndirect(sizeof(GLSLDispatchCommand) * (1 - pingPongIndex));
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);
            }

            finalDrawProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);

            AccumulatedSamples++;
        }

        public void ResetRender()
        {
            AccumulatedSamples = 0;
        }

        public unsafe void SetSize(int width, int height)
        {
            dispatchCommandBuffer.SubData(0, sizeof(GLSLDispatchCommand), new GLSLDispatchCommand() { NumGroupsX = 0, NumGroupsY = 1, NumGroupsZ = 1 });
            dispatchCommandBuffer.SubData(sizeof(GLSLDispatchCommand), sizeof(GLSLDispatchCommand), new GLSLDispatchCommand() { NumGroupsX = 0, NumGroupsY = 1, NumGroupsZ = 1 });

            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba32f);

            if (transportRayBuffer != null) transportRayBuffer.Dispose();
            transportRayBuffer = new BufferObject();
            transportRayBuffer.ImmutableAllocate(width * height * sizeof(GLSLTransportRay), IntPtr.Zero, BufferStorageFlags.None);
            transportRayBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6);

            if (rayIndicesBuffer != null) rayIndicesBuffer.Dispose();
            rayIndicesBuffer = new BufferObject();
            rayIndicesBuffer.ImmutableAllocate(width * height * sizeof(uint) + 3 * sizeof(uint), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            rayIndicesBuffer.SubData(0, sizeof(Vector3i), new Vector3i(0));
            rayIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7);
        }

        public void Dispose()
        {
            Result.Dispose();
            
            firstHitProgram.Dispose();
            nHitProgram.Dispose();
            finalDrawProgram.Dispose();
            
            dispatchCommandBuffer.Dispose();
            transportRayBuffer.Dispose();
            rayIndicesBuffer.Dispose();
        }
    }
}
