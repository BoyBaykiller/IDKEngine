using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class PathTracer
    {
        private int cachedRayDepth;
        public int RayDepth;

        private float _focalLength;
        public float FocalLength
        {
            get => _focalLength;

            set
            {
                _focalLength = value;
                firstHitProgram.Upload("FocalLength", value);
            }
        }

        private float _apertureDiameter;
        public float ApertureDiameter
        {
            get => _apertureDiameter;

            set
            {
                _apertureDiameter = value;
                firstHitProgram.Upload("ApertureDiameter", value);
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
                if (_isDebugBVHTraversal)
                {
                    firstHitProgram.Upload("ApertureDiameter", 0.0f);
                    cachedRayDepth = RayDepth; 
                    RayDepth = 1;
                }
                else
                {
                    ApertureDiameter = _apertureDiameter;
                    RayDepth = cachedRayDepth;
                }
            }
        }

        private bool _isRNGFrameBased;
        public bool IsRNGFrameBased
        {
            get => _isRNGFrameBased;

            set
            {
                _isRNGFrameBased = value;
                firstHitProgram.Upload("IsRNGFrameBased", _isRNGFrameBased);
                nHitProgram.Upload("IsRNGFrameBased", _isRNGFrameBased);
            }
        }

        public ModelSystem ModelSystem;
        public Texture Result;
        public readonly BVH BVH;
        private readonly ShaderProgram firstHitProgram;
        private readonly ShaderProgram nHitProgram;
        private readonly ShaderProgram finalDrawProgram;
        private readonly BufferObject dispatchCommandBuffer;
        private readonly BufferObject aliveRaysBuffer;
        private readonly Texture skyBox;
        private BufferObject transportRayBuffer;
        private BufferObject rayIndicesBuffer;
        public unsafe PathTracer(BVH bvh, ModelSystem modelSystem, Texture skyBox, int width, int height)
        {
            firstHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/FirstHit/compute.glsl")));
            nHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/NHit/compute.glsl")));
            finalDrawProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/FinalDraw/compute.glsl")));

            SetSize(width, height);

            dispatchCommandBuffer = new BufferObject();
            dispatchCommandBuffer.ImmutableAllocate(sizeof(GLSLDispatchCommand), new Vector3i(1), BufferStorageFlags.DynamicStorageBit);
            dispatchCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 8);

            aliveRaysBuffer = new BufferObject();
            aliveRaysBuffer.ImmutableAllocate(sizeof(uint), 0u, BufferStorageFlags.DynamicStorageBit);
            aliveRaysBuffer.BindBufferBase(BufferRangeTarget.AtomicCounterBuffer, 0);

            this.skyBox = skyBox;
            ModelSystem = modelSystem;
            BVH = bvh;

            RayDepth = 6;
            FocalLength = 10.0f;
            ApertureDiameter = 0.03f;
        }

        public unsafe void Compute()
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba32f);
            skyBox.BindToUnit(0);

            uint maxPossibleRayCount = (uint)(Result.Width * Result.Height);
            uint maxPossibleWorkGroupSizeX = (uint)Math.Ceiling(Result.Width * Result.Height / 64.0f);
            aliveRaysBuffer.SubData(0, sizeof(uint), maxPossibleRayCount);
            dispatchCommandBuffer.SubData(0, sizeof(uint), maxPossibleWorkGroupSizeX);
            dispatchCommandBuffer.Bind(BufferTarget.DispatchIndirectBuffer);

            rayIndicesBuffer.SubData(0, sizeof(uint), 0u);

            firstHitProgram.Use();
            GL.DispatchComputeIndirect((IntPtr)0);

            nHitProgram.Use();
            for (int i = 1; i < RayDepth; i++)
            {
                const uint rayIndicesLength = 0u;
                rayIndicesBuffer.SubData(0, sizeof(uint), rayIndicesLength);

                GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit | MemoryBarrierFlags.AtomicCounterBarrierBit);
                GL.DispatchComputeIndirect((IntPtr)0);
            }

            finalDrawProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public unsafe void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba32f);

            if (transportRayBuffer != null) transportRayBuffer.Dispose();
            transportRayBuffer = new BufferObject();
            transportRayBuffer.ImmutableAllocate(width * height * sizeof(GLSLTransportRay), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            transportRayBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6);

            if (rayIndicesBuffer != null) rayIndicesBuffer.Dispose();
            rayIndicesBuffer = new BufferObject();
            rayIndicesBuffer.ImmutableAllocate(width * height * sizeof(uint) + sizeof(uint), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            rayIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7);
        }
    }
}
