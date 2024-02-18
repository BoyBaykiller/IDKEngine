using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using FFX_FSR2;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class FSR2Wrapper : IDisposable
    {
        public static readonly bool IS_FSR2_SUPPORTED = OperatingSystem.IsWindows();

        public Texture Result;
        public FSR2Wrapper(int outputWidth, int outputHeight, int maxInputWidth, int maxInputHeight)
        {
            if (!IS_FSR2_SUPPORTED)
            {
                Logger.Log(Logger.LogLevel.Fatal, $"{nameof(IS_FSR2_SUPPORTED)} was {IS_FSR2_SUPPORTED}. FSR2 is Windows only");
                Environment.Exit(0);
            }

            SetSize(outputWidth, outputHeight, maxInputWidth, maxInputHeight);
        }

        public bool IsSharpening;
        public float Sharpness = 0.5f;
        public void RunFSR2(Vector2 jitter, Texture color, Texture depth, Texture velocity, float deltaMilliseconds, float cameraNear, float cameraFar, float cameraFovAngleVertical)
        {
            Vector2i renderSize = new Vector2i(color.Width, color.Height);

            Vector2 jitterUsedThisFrame = jitter / 2.0f * renderSize;
            FSR2.DispatchDescription dispatchDesc = new FSR2.DispatchDescription()
            {
                Color = FSR2.GL.GetTextureResource((uint)color.ID, (uint)color.Width, (uint)color.Height, (uint)color.SizedInternalFormat),
                Depth = FSR2.GL.GetTextureResource((uint)depth.ID, (uint)depth.Width, (uint)depth.Height, (uint)depth.SizedInternalFormat),
                MotionVectors = FSR2.GL.GetTextureResource((uint)velocity.ID, (uint)velocity.Width, (uint)velocity.Height, (uint)velocity.SizedInternalFormat),
                Exposure = new FSR2Types.Resource(),
                Reactive = new FSR2Types.Resource(),
                TransparencyAndComposition = new FSR2Types.Resource(),
                Output = FSR2.GL.GetTextureResource((uint)Result.ID, (uint)Result.Width, (uint)Result.Height, (uint)Result.SizedInternalFormat),
                JitterOffset = new FSR2Types.FloatCoords2D() { X = jitterUsedThisFrame.X, Y = jitterUsedThisFrame.Y },
                MotionVectorScale = new FSR2Types.FloatCoords2D() { X = -renderSize.X, Y = -renderSize.Y },
                RenderSize = new FSR2Types.Dimensions2D() { Width = (uint)renderSize.X, Height = (uint)renderSize.Y },
                EnableSharpening = IsSharpening ? (byte)1 : (byte)0,
                Sharpness = Sharpness,
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

        }

        private FSR2.Context fsr2Context;
        private byte[] fsr2ScratchMemory;
        private bool isFsr2Initialized = false;
        public unsafe void SetSize(int inputWidth, int inputHeight, int outputWidth, int outputHeight)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(outputWidth, outputHeight, 1, SizedInternalFormat.Rgba16f);


            if (isFsr2Initialized)
            {
                FSR2.ContextDestroy(ref fsr2Context);
            }

            FSR2.ContextDescription contextDesc = new FSR2.ContextDescription
            {
                Flags = FSR2.InitializationFlagBits.EnableHighDynamicRange | FSR2.InitializationFlagBits.EnableAutoExposure | FSR2.InitializationFlagBits.EnableDebugChecking | FSR2.InitializationFlagBits.AllowNullDeviceAndCommandList,
                MaxRenderSize = new FSR2Types.Dimensions2D() { Width = (uint)inputWidth, Height = (uint)inputHeight },
                DisplaySize = new FSR2Types.Dimensions2D() { Width = (uint)outputWidth, Height = (uint)outputHeight },
                FpMessage = (delegate* unmanaged<FSR2Interface.MsgType, string, void>)Marshal.GetFunctionPointerForDelegate((FSR2.FpMessageDelegate)Fsr2Message),
            };
            fsr2ScratchMemory = new byte[FSR2.GL.GetScratchMemorySize()];
            FSR2.GL.GetInterface(out contextDesc.Callbacks, ref fsr2ScratchMemory[0], (nuint)fsr2ScratchMemory.Length, GLFW.GetProcAddress);
            FSR2.ContextCreate(out fsr2Context, contextDesc);
            isFsr2Initialized = true;
        }

        public void Dispose()
        {
            FSR2.ContextDestroy(ref fsr2Context);
            Result.Dispose();
        }

        public static int GetRecommendedSampleCount(int renderWidth, int displayWith)
        {
            return FSR2.GetJitterPhaseCount(renderWidth, displayWith);
        }

        public static float GetRecommendedMipmapBias(int renderWidth, int displayWith)
        {
            return MathF.Log2((float)renderWidth / displayWith) - 1.0f;
        }

        private static void Fsr2Message(FSR2Interface.MsgType type, string message)
        {
            Logger.Log(Logger.LogLevel.Info, $"FSR2: {type} {message}");
        }
    }
}
