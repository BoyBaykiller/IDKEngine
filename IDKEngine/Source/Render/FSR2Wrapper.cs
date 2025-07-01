using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using FFX_FSR2;
using BBLogger;
using BBOpenGL;

namespace IDKEngine.Render;

class FSR2Wrapper : IDisposable
{
    public static readonly bool IS_SUPPORTED = OperatingSystem.IsWindows();

    public bool IsSharpening;
    public float Sharpness = 0.5f;

    public BBG.Texture Result;

    private FSR2.Context fsr2Context;
    private byte[] fsr2ScratchMemory;
    private bool isFsr2Initialized;

    public FSR2Wrapper(Vector2i maxInputSize, Vector2i outputSize)
    {
        if (!IS_SUPPORTED)
        {
            Logger.Log(Logger.LogLevel.Fatal, $"FSR2 is Windows only");
            Environment.Exit(0);
        }

        SetSize(maxInputSize, outputSize);
        IsSharpening = true;
    }

    public void Run(BBG.Texture color, BBG.Texture depth, BBG.Texture velocity, Camera camera, Vector2 jitter, float deltaMs)
    {
        BBG.Debugging.PushDebugGroup("Run FSR2");

        Vector2i renderSize = new Vector2i(color.Width, color.Height);
        jitter = jitter / 2.0f * renderSize;

        FSR2.DispatchDescription dispatchDesc = new FSR2.DispatchDescription()
        {
            Color = FSR2.GL.GetTextureResource((uint)color.ID, (uint)color.Width, (uint)color.Height, (uint)color.Format),
            Depth = FSR2.GL.GetTextureResource((uint)depth.ID, (uint)depth.Width, (uint)depth.Height, (uint)depth.Format),
            MotionVectors = FSR2.GL.GetTextureResource((uint)velocity.ID, (uint)velocity.Width, (uint)velocity.Height, (uint)velocity.Format),
            Exposure = new FSR2Types.Resource(),
            Reactive = new FSR2Types.Resource(),
            TransparencyAndComposition = new FSR2Types.Resource(),
            Output = FSR2.GL.GetTextureResource((uint)Result.ID, (uint)Result.Width, (uint)Result.Height, (uint)Result.Format),
            JitterOffset = new FSR2Types.FloatCoords2D() { X = jitter.X, Y = jitter.Y },
            MotionVectorScale = new FSR2Types.FloatCoords2D() { X = -renderSize.X, Y = -renderSize.Y },
            RenderSize = new FSR2Types.Dimensions2D() { Width = (uint)renderSize.X, Height = (uint)renderSize.Y },
            EnableSharpening = IsSharpening ? (byte)1 : (byte)0,
            Sharpness = Sharpness,
            FrameTimeDelta = deltaMs,
            PreExposure = 1.0f,
            Reset = 0,
            CameraNear = camera.NearPlane,
            CameraFar = camera.FarPlane,
            CameraFovAngleVertical = camera.FovY,
            ViewSpaceToMetersFactor = 1,
        };
        FSR2.ContextDispatch(ref fsr2Context, dispatchDesc);
        BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit);

        BBG.Debugging.PopDebugGroup();
    }

    public unsafe void SetSize(Vector2i inputSize, Vector2i outputSize)
    {
        if (Result != null) Result.Dispose();
        Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
        Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        Result.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        Result.Allocate(outputSize.X, outputSize.Y, 1, BBG.Texture.InternalFormat.R16G16B16A16Float);

        if (isFsr2Initialized)
        {
            FSR2.ContextDestroy(ref fsr2Context);
        }

        FSR2.ContextDescription contextDesc = new FSR2.ContextDescription
        {
            Flags = FSR2.InitializationFlagBits.EnableHighDynamicRange | FSR2.InitializationFlagBits.EnableAutoExposure | FSR2.InitializationFlagBits.EnableDebugChecking | FSR2.InitializationFlagBits.AllowNullDeviceAndCommandList,
            MaxRenderSize = new FSR2Types.Dimensions2D() { Width = (uint)inputSize.X, Height = (uint)inputSize.Y },
            DisplaySize = new FSR2Types.Dimensions2D() { Width = (uint)outputSize.X, Height = (uint)outputSize.Y },
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
        Logger.Log(Logger.LogLevel.Warn, $"FSR2: {type} {message}");
    }
}
