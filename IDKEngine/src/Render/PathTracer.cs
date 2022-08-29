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
                firstHitProgram.Upload("IsDebugBVHTraversal", _isDebugBVHTraversal);
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
            }
        }

        public ModelSystem ModelSystem;
        public Texture Result;
        public readonly BVH BVH;
        private readonly ShaderProgram firstHitProgram;
        private readonly ShaderProgram nHitProgram;
        private readonly ShaderProgram finalDrawProgram;
        private readonly BufferObject dispatchCommandBuffer;
        private readonly Texture skyBox;
        private BufferObject transportRayBuffer;
        private BufferObject rayIndicesBuffer;
        public unsafe PathTracer(BVH bvh, ModelSystem modelSystem, Texture skyBox, int width, int height)
        {
            string firstHitProgramSrc = File.ReadAllText("res/shaders/PathTracing/FirstHit/compute.glsl");
            firstHitProgramSrc = firstHitProgramSrc.Replace("__maxBlasTreeDepth__", $"{bvh.MaxBlasTreeDepth}");
            firstHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, firstHitProgramSrc));

            string nHitProgramSrc = File.ReadAllText("res/shaders/PathTracing/NHit/compute.glsl");
            nHitProgramSrc = nHitProgramSrc.Replace("__maxBlasTreeDepth__", $"{bvh.MaxBlasTreeDepth}");
            nHitProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, nHitProgramSrc));

            finalDrawProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PathTracing/FinalDraw/compute.glsl")));

            dispatchCommandBuffer = new BufferObject();
            dispatchCommandBuffer.ImmutableAllocate(2 * sizeof(GLSLDispatchCommand), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            dispatchCommandBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 8);

            SetSize(width, height);

            this.skyBox = skyBox;
            ModelSystem = modelSystem;
            BVH = bvh;

            RayDepth = 7;
            FocalLength = 8.0f;
            ApertureDiameter = 0.02f;
        }

        public unsafe void Compute()
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba32f);
            skyBox.BindToUnit(0);

            firstHitProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);

            dispatchCommandBuffer.Bind(BufferTarget.DispatchIndirectBuffer);
            nHitProgram.Use();
            for (int i = 1; i < RayDepth; i++)
            {
                int pingPongIndex = 1 - (i % 2);
                nHitProgram.Upload(0, pingPongIndex);
                rayIndicesBuffer.SubData(pingPongIndex * sizeof(uint), sizeof(uint), 0u); // set count

                GL.DispatchComputeIndirect((IntPtr)(sizeof(GLSLDispatchCommand) * (1 - pingPongIndex)));
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);
            }

            finalDrawProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.CommandBarrierBit);
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
            transportRayBuffer.ImmutableAllocate(width * height * sizeof(GLSLTransportRay), (IntPtr)0, BufferStorageFlags.None);
            transportRayBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6);

            if (rayIndicesBuffer != null) rayIndicesBuffer.Dispose();
            rayIndicesBuffer = new BufferObject();
            rayIndicesBuffer.ImmutableAllocate(width * height * sizeof(uint) + 2 * sizeof(uint), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            rayIndicesBuffer.SubData(0, sizeof(Vector2i), new Vector2i(0));
            rayIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7);
        }
    }
}
