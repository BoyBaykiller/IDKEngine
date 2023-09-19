using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render.Objects;
using FFX_FSR2;

namespace IDKEngine.Render
{
    unsafe class TAAResolve : IDisposable
    {
        public bool TaaIsEnabled
        {
            get => taaData.IsEnabled;

            set
            {
                taaData.IsEnabled = value;

                if (!taaData.IsEnabled)
                {
                    taaData.Jitter = new Vector2(0.0f);
                    taaDataBuffer.SubData(0, sizeof(GLSLTaaData), taaData);
                }
            }
        }

        public int TaaSamples
        {
            get => taaData.Samples;

            set
            {
                Debug.Assert(value <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);
                taaData.Samples = value;
            }
        }

        private bool _isTaaArtifactMitigation;
        public bool IsTaaArtifactMitigation
        {
            get => _isTaaArtifactMitigation;

            set
            {
                _isTaaArtifactMitigation = value;
                taaResolveProgram.Upload("IsTaaArtifactMitigation", _isTaaArtifactMitigation);
            }
        }

        public Texture Result => (taaData.Frame % 2 == 0) ? taaPing : taaPong;
        public Texture PrevResult => (taaData.Frame % 2 == 0) ? taaPong : taaPing;

        private Texture taaPing;
        private Texture taaPong;
        private readonly ShaderProgram taaResolveProgram;
        public readonly BufferObject taaDataBuffer;
        private GLSLTaaData taaData;
        public TAAResolve(int width, int height, int taaSamples = 6)
        {
            Debug.Assert(taaSamples <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);

            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TAAResolve/compute.glsl")));

            taaDataBuffer = new BufferObject();
            taaDataBuffer.ImmutableAllocate(sizeof(GLSLTaaData), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

            taaData = new GLSLTaaData();
            taaData.Samples = taaSamples;
            taaData.IsEnabled = true;
            taaData.Frame = 0;
            taaDataBuffer.SubData(0, sizeof(GLSLTaaData), taaData);

            SetSize(width, height);
            IsTaaArtifactMitigation = true;
        }

        public void RunTAAResolve(Texture color)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            PrevResult.BindToUnit(0);
            color.BindToUnit(1);
            
            taaResolveProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            if (taaData.IsEnabled)
            {
                Vector2i renderSize = new Vector2i(color.Width, color.Height);
                Vector2 jitter = MyMath.GetHalton2D(taaData.Frame % taaData.Samples, 2, 3);

                taaData.Jitter = (jitter * 2.0f - new Vector2(1.0f)) / renderSize;
            }
            taaData.Frame++;
            taaDataBuffer.SubData(0, sizeof(GLSLTaaData), taaData);
        }

        public void RunFSR2(Texture color, Texture depth, Texture velocity, float deltaMilliseconds, float cameraNear, float cameraFar, float cameraFovAngleVertical)
        {
            Vector2i renderSize = new Vector2i(color.Width, color.Height);
            Vector2i displaySize = new Vector2i(Result.Width, Result.Height);

            // make upscaling werk plis
            // investigate jitter bug when activating vxgi

            // FSR2 wants jitter to be in [-0.5, 0.5] space
            int sampleCount = FSR2.GetJitterPhaseCount(renderSize.X, displaySize.X);
            Vector2 jitter = MyMath.GetHalton2D(taaData.Frame % sampleCount, 2, 3) - new Vector2(0.5f);
            FSR2.DispatchDescription dispatchDesc = new FSR2.DispatchDescription()
            {
                Color = FSR2.GL.GetTextureResource((uint)color.ID, (uint)color.Width, (uint)color.Height, (uint)color.SizedInternalFormat),
                Depth = FSR2.GL.GetTextureResource((uint)depth.ID, (uint)depth.Width, (uint)depth.Height, (uint)depth.SizedInternalFormat),
                MotionVectors = FSR2.GL.GetTextureResource((uint)velocity.ID, (uint)velocity.Width, (uint)velocity.Height, (uint)velocity.SizedInternalFormat),
                Exposure = new FSR2Types.Resource(),
                Reactive = new FSR2Types.Resource(),
                TransparencyAndComposition = new FSR2Types.Resource(),
                Output = FSR2.GL.GetTextureResource((uint)Result.ID, (uint)Result.Width, (uint)Result.Height, (uint)Result.SizedInternalFormat),
                JitterOffset = new FSR2Types.FloatCoords2D() { X = jitter.X, Y = jitter.Y },
                MotionVectorScale = new FSR2Types.FloatCoords2D() { X = -renderSize.X, Y = -renderSize.Y },
                RenderSize = new FSR2Types.Dimensions2D() { Width = (uint)renderSize.X, Height = (uint)renderSize.Y },
                EnableSharpening = 0,
                Sharpness = 0.0f,
                FrameTimeDelta = deltaMilliseconds,
                PreExposure = 1.0f,
                Reset = 0,
                CameraNear = cameraNear,
                CameraFar = cameraFar,
                CameraFovAngleVertical = cameraFovAngleVertical,
                ViewSpaceToMetersFactor = 1,
                DeviceDepthNegativeOneToOne = 0,
            };
            FSR2.ContextDispatch(ref fsr2Context, dispatchDesc);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            taaData.Jitter = 2.0f * jitter / renderSize;
            taaData.Frame++;
            taaDataBuffer.SubData(0, sizeof(GLSLTaaData), taaData);
        }

        private FSR2.Context fsr2Context;
        private byte[] fsr2ScratchMemory;
        private bool isFsr2Initialized = false;
        public void SetSize(int width, int height)
        {
            if (taaPing != null) taaPing.Dispose();
            taaPing = new Texture(TextureTarget2d.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            if (taaPong != null) taaPong.Dispose();
            taaPong = new Texture(TextureTarget2d.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            {
                static void Fsr2Message(FSR2Interface.MsgType type, string message)
                {
                    Console.WriteLine($"{type}: {message}");
                }
                FSR2.FpMessageDelegate fsr2MessageFuncPtr = Fsr2Message;

                if (isFsr2Initialized)
                {
                    FSR2.ContextDestroy(ref fsr2Context);
                }

                FSR2.ContextDescription contextDesc = new FSR2.ContextDescription
                {
                    Flags = FSR2.InitializationFlagBits.EnableHighDynamicRange | FSR2.InitializationFlagBits.EnableAutoExposure | FSR2.InitializationFlagBits.EnableDebugChecking | FSR2.InitializationFlagBits.AllowNullDeviceAndCommandList,
                    MaxRenderSize = new FSR2Types.Dimensions2D() { Width = (uint)(Result.Width * Application.debugResScale), Height = (uint)(Result.Height * Application.debugResScale) },
                    DisplaySize = new FSR2Types.Dimensions2D() { Width = (uint)Result.Width, Height = (uint)Result.Height },
                    FpMessage = (delegate* unmanaged<FSR2Interface.MsgType, string, void>)Marshal.GetFunctionPointerForDelegate(fsr2MessageFuncPtr),
                };
                fsr2ScratchMemory = new byte[FSR2.GL.GetScratchMemorySize()];
                FSR2.GL.GetInterface(out contextDesc.Callbacks, ref fsr2ScratchMemory[0], (nuint)fsr2ScratchMemory.Length, GLFW.GetProcAddress);
                FSR2.ContextCreate(out fsr2Context, contextDesc);
                isFsr2Initialized = true;
            }
        }

        public void Dispose()
        {
            taaPing.Dispose();
            taaPong.Dispose();
            taaResolveProgram.Dispose();
            taaDataBuffer.Dispose();
        }
    }
}
