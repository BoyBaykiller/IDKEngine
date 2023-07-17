using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Render.Objects;
//using FFX_FSR2;

namespace IDKEngine.Render
{
    unsafe class TAAResolve : IDisposable
    {
        public bool TaaEnabled
        {
            get => taaData.IsEnabled == 1;

            set
            {
                taaData.IsEnabled = value ? 1 : 0;
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

        public float TaaVelScale
        {
            get => taaData.VelScale;

            set
            {
                taaData.VelScale = value;
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

        public Texture Result => isPing ? taaPing : taaPong;
        public Texture PrevResult => isPing ? taaPong : taaPing;

        private Texture taaPing;
        private Texture taaPong;
        private readonly ShaderProgram taaResolveProgram;
        private readonly BufferObject taaDataBuffer;
        private GLSLTaaData taaData;
        private bool isPing;
        public TAAResolve(int width, int height, int taaSamples = 6)
        {
            Debug.Assert(taaSamples <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);

            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TAAResolve/compute.glsl")));

            taaDataBuffer = new BufferObject();
            taaDataBuffer.ImmutableAllocate(sizeof(GLSLTaaData), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            taaDataBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

            taaData = new GLSLTaaData();
            taaData.Samples = taaSamples;
            taaData.IsEnabled = 1;
            taaData.VelScale = 5.0f;
            taaData.Frame = 0;
            taaDataBuffer.SubData(0, sizeof(GLSLTaaData), taaData);

            SetSize(width, height);
            IsTaaArtifactMitigation = true;
        }


        public void RunTAAResolve(Texture color)
        {
            if (taaData.IsEnabled == 0)
            {
                GL.CopyImageSubData(color.ID, ImageTarget.Texture2D, 0, 0, 0, 0, Result.ID, ImageTarget.Texture2D, 0, 0, 0, 0, Result.Width, Result.Height, 1);
                return;
            }

            taaData.Jitter = jitterSequence[taaData.Frame % taaData.Samples];
            taaDataBuffer.SubData(0, sizeof(GLSLTaaData), taaData);
            taaData.Frame++;


            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, Result.SizedInternalFormat);
            color.BindToUnit(0);
            PrevResult.BindToUnit(1);

            taaResolveProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            isPing = !isPing;
        }

        public void RunFSR2(Texture color, Texture depth, Texture velocity, float deltaMilliseconds, float cameraNear, float cameraFar, float cameraFovAngleVertical)
        {
            //FSR2.GetJitterOffset(out float jitterX, out float jitterY, (int)taaData.Frame, taaData.Samples);
            //jitterX = 2.0f * jitterX / taaPing.Width;
            //jitterY = 2.0f * jitterY / taaPing.Height;
            //taaData.Jitter = new Vector2(jitterX, jitterY);
            //taaDataBuffer.SubData(0, sizeof(GLSLTaaData), taaData);
            //taaData.Frame++;

            //FSR2.DispatchDescription dispatchDesc = new FSR2.DispatchDescription()
            //{
            //    Color = FSR2.GL.GetTextureResource((uint)color.ID, (uint)color.Width, (uint)color.Height, (uint)color.SizedInternalFormat),
            //    Depth = FSR2.GL.GetTextureResource((uint)depth.ID, (uint)depth.Width, (uint)depth.Height, (uint)depth.SizedInternalFormat),
            //    MotionVectors = FSR2.GL.GetTextureResource((uint)velocity.ID, (uint)velocity.Width, (uint)velocity.Height, (uint)velocity.SizedInternalFormat),
            //    Exposure = new FSR2Types.Resource(),
            //    Reactive = new FSR2Types.Resource(),
            //    TransparencyAndComposition = new FSR2Types.Resource(),
            //    Output = FSR2.GL.GetTextureResource((uint)Result.ID, (uint)Result.Width, (uint)Result.Height, (uint)Result.SizedInternalFormat),
            //    JitterOffset = new FSR2Types.FloatCoords2D() { X = jitterX, Y = jitterY },
            //    MotionVectorScale = new FSR2Types.FloatCoords2D() { X = taaData.VelScale, Y = taaData.VelScale },
            //    RenderSize = new FSR2Types.Dimensions2D() { Width = (uint)Result.Width, Height = (uint)Result.Height },
            //    EnableSharpening = 0,
            //    Sharpness = 0.0f,
            //    FrameTimeDelta = deltaMilliseconds,
            //    PreExposure = 1.0f,
            //    Reset = 0,
            //    CameraNear = cameraNear,
            //    CameraFar = cameraFar,
            //    CameraFovAngleVertical = cameraFovAngleVertical,
            //    ViewSpaceToMetersFactor = 1,
            //    DeviceDepthNegativeOneToOne = 0,
            //};
            //FSR2CheckError(FSR2.ContextDispatch(ref fsr2Context, dispatchDesc));
            //GL.Finish();
            //GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);
        }

        private Vector2[] jitterSequence;
        //private bool isFsr2Initialized = false;
        //private FSR2.Context fsr2Context = new FSR2.Context();
        //private byte[] fsr2ScratchMemory;
        public void SetSize(int width, int height)
        {
            if (taaPing != null) taaPing.Dispose();
            taaPing = new Texture(TextureTarget2d.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba8);

            if (taaPong != null) taaPong.Dispose();
            taaPong = new Texture(TextureTarget2d.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba8);

            jitterSequence = new Vector2[GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT];
            MyMath.GetHaltonSequence_2_3(jitterSequence);
            MyMath.MapHaltonSequence(jitterSequence, width, height);

            //{
            //    static void Fsr2Message(FSR2Interface.MsgType type, string message)
            //    {
            //        Console.WriteLine($"{type}: {message}");
            //    }
            //    FSR2.DelegateMessage delegateMessage = Fsr2Message;

            //    if (isFsr2Initialized)
            //    {
            //        FSR2CheckError(FSR2.ContextDestroy(ref fsr2Context));
            //    }

            //    FSR2.ContextDescription contextDesc = new FSR2.ContextDescription
            //    {
            //        Flags = FSR2.InitializationFlagBits.EnableDebugChecking | FSR2.InitializationFlagBits.EnableAutoExposure | FSR2.InitializationFlagBits.EnableHighDynamicRange | FSR2.InitializationFlagBits.AllowNullDeviceAndCommandList,
            //        MaxRenderSize = new FSR2Types.Dimensions2D() { Width = (uint)Result.Width, Height = (uint)Result.Height },
            //        DisplaySize = new FSR2Types.Dimensions2D() { Width = (uint)Result.Width, Height = (uint)Result.Height },
            //        FpMessage = (delegate* unmanaged<FSR2Interface.MsgType, string, void>)Marshal.GetFunctionPointerForDelegate(delegateMessage),
            //    };
            //    fsr2ScratchMemory = new byte[FSR2.GL.GetScratchMemorySize()];
            //    FSR2CheckError(FSR2.GL.GetInterface(out contextDesc.Callbacks, ref fsr2ScratchMemory[0], (nuint)fsr2ScratchMemory.Length, GLFW.GetProcAddress));

            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpCreateBackendContext);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpGetDeviceCapabilities);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpDestroyBackendContext);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpCreateResource);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpRegisterResource);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpUnregisterResources);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpGetResourceDescription);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpDestroyResource);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpCreatePipeline);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpDestroyPipeline);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpScheduleGpuJob);
            //    //Console.WriteLine((uint)contextDesc.Callbacks.FpExecuteGpuJobs);
            //    //Console.WriteLine((ulong)contextDesc.Callbacks.ScratchBuffer);
            //    //Console.WriteLine(contextDesc.Callbacks.ScratchBufferSize);
            //    //Console.WriteLine($"Sizeof(FSR2Types.PipelineState): {sizeof(FSR2Types.PipelineState)}"); // 3568
            //    //Console.WriteLine($"Sizeof(FSR2Types.GpuJobDescription): {sizeof(FSR2Types.GpuJobDescription)}"); // 7576
            //    //Console.WriteLine($"Sizeof(FSR2Types.ComputeJobDescription): {sizeof(FSR2Types.ComputeJobDescription)}"); // 7568
            //    //Console.WriteLine($"Sizeof(FSR2Types.ClearFloatJobDescription): {sizeof(FSR2Types.ClearFloatJobDescription)}"); // 20
            //    //Console.WriteLine($"Sizeof(FSR2Types.CopyJobDescription): {sizeof(FSR2Types.CopyJobDescription)}"); // 8    
            //    //Console.WriteLine($"Sizeof(FSR2Types.ResourceInternal): {sizeof(FSR2Types.ResourceInternal)}"); // 4
            //    //Console.WriteLine($"Sizeof(FSR2Types.ResourceBinding): {sizeof(FSR2Types.ResourceBinding)}"); // 136
            //    //Console.WriteLine($"Sizeof(FSR2Types.ContextDescription): {sizeof(FSR2.ContextDescription)}"); // 152
            //    //Console.WriteLine($"Sizeof(FSR2Types.DeviceCapabilities): {sizeof(FSR2Types.DeviceCapabilities)}"); // 16
            //    //Console.WriteLine($"Sizeof(FSR2Types.CreateResourceDescription): {sizeof(FSR2Types.CreateResourceDescription)}"); // 64
            //    //Console.WriteLine($"Sizeof(FSR2Types.ResourceDescription): {sizeof(FSR2Types.ResourceDescription)}"); // 28
            //    //Console.WriteLine($"Sizeof(FSR2Types.PipelineDescription): {sizeof(FSR2Types.PipelineDescription)}"); // 40
            //    //Console.WriteLine($"Sizeof(FSR2Types.Resource): {sizeof(FSR2Types.Resource)}"); // 184
            //    //Console.WriteLine($"Sizeof(FSR2Types.ConstantBuffer): {sizeof(FSR2Types.ConstantBuffer)}"); // 260
            //    //Console.WriteLine($"Sizeof(FSR2Types.FloatCoords2D): {sizeof(FSR2Types.FloatCoords2D)}"); // 8
            //    //Console.WriteLine($"Sizeof(FSR2Types.Dimensions2D): {sizeof(FSR2Types.Dimensions2D)}"); // 8
            //    //Console.WriteLine($"Sizeof(FSR2.ContextDescription): {sizeof(FSR2.ContextDescription)}"); // 152
            //    //Console.WriteLine($"Sizeof(FSR2.DispatchDescription): {sizeof(FSR2.DispatchDescription)}"); // 1560
            //    //Console.WriteLine($"Sizeof(FSR2.Context): {sizeof(FSR2.Context)}"); // 66144
            //    //Console.WriteLine($"Sizeof(FSR2Interface.Interface): {sizeof(FSR2Interface.Interface)}"); // 112

            //    FSR2CheckError(FSR2.ContextCreate(out fsr2Context, contextDesc));
            //    isFsr2Initialized = true;
            //}
        }

        //static void FSR2CheckError(FSR2Error.ErrorCode code)
        //{
        //    if (code != FSR2Error.ErrorCode.OK)
        //    {
        //        Console.WriteLine("wtf " + code);
        //    }
        //}

        public void Dispose()
        {
            taaPing.Dispose();
            taaPong.Dispose();
            taaResolveProgram.Dispose();
            taaDataBuffer.Dispose();
        }
    }
}
