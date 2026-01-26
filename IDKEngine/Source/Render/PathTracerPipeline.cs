using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.IntelOpenImageDenoise;

namespace IDKEngine.Render;

class PathTracerPipeline : IDisposable
{
    public enum OutputTexture : int
    {
        Noisy,
        Denoised,
        Albedo,
        Normal,
    }

    private unsafe struct OIDNContext
    {
        // https://github.com/RenderKit/oidn

        public void* Device;
        public void* BeautyFilter;

        // For PreFiltering
        public void* AlbedoFilter;
        public void* NormalFilter;

        public void* BeautyBuffer;
        public void* AlbedoBuffer;
        public void* NormalBuffer;
        public void* OutputBuffer;

        [UnmanagedCallersOnly]
        public static unsafe void ErrorCallback(void* device, OIDN.Error error, byte* message)
        {
            Logger.Log(Logger.LogLevel.Warn, $"OIDN: {error} {Marshal.PtrToStringAnsi((nint)message)}");
        }
    }

    public Vector2i RenderResolution => new Vector2i(pathTracer.Result.Width, pathTracer.Result.Height);

    public int AccumulatedSamples => (int)pathTracer.AccumulatedSamples;

    public BBG.Texture Result
    {
        get
        {
            BBG.Texture result = PathTracerOutput switch
            {
                OutputTexture.Noisy => pathTracer.Result,
                OutputTexture.Denoised => denoisedTexture,
                OutputTexture.Albedo => pathTracer.AlbedoTexture,
                OutputTexture.Normal => pathTracer.NormalTexture,
            };

            if (DoDebugBVHTraversal)
            {
                result = pathTracer.Result;
            }

            return result;
        }
    }

    public int SamplesPerPixel
    {
        get => pathTracer.SamplesPerPixel;
        set => pathTracer.SamplesPerPixel = value;
    }

    public int RayDepth
    {
        get => pathTracer.RayDepth;
        set => pathTracer.RayDepth = value;
    }

    public float FocalLength
    {
        get => pathTracer.FocalLength;
        set => pathTracer.FocalLength = value;
    }

    public float LenseRadius
    {
        get => pathTracer.LenseRadius;
        set => pathTracer.LenseRadius = value;
    }

    public bool DoDebugBVHTraversal
    {
        get => pathTracer.DoDebugBVHTraversal;
        set => pathTracer.DoDebugBVHTraversal = value;
    }

    public bool DoTraceLights
    {
        get => pathTracer.DoTraceLights;
        set => pathTracer.DoTraceLights = value;
    }

    public bool DoRussianRoulette
    {
        get => pathTracer.DoRussianRoulette;
        set => pathTracer.DoRussianRoulette = value;
    }

    public bool DoRaySorting
    {
        get => pathTracer.DoRaySorting;
        set => pathTracer.DoRaySorting = value;
    }

    private bool _denoisingEnabled;
    public bool DenoisingEnabled
    {
        get => _denoisingEnabled;

        set
        {
            _denoisingEnabled = value;
            pathTracer.OutputAOVs = DenoisingEnabled;

            // We need to reset because the AOVs need to be accumulated.
            ResetAccumulation();

            if (!DenoisingEnabled)
            {
                PathTracerOutput = OutputTexture.Noisy;
            }
        }
    }

    public bool DoPrefiltering;

    public OutputTexture PathTracerOutput;
    public int AutoDenoiseSamplesThreshold;

    private BBG.Texture denoisedTexture;
    private readonly PathTracer pathTracer;
    private OIDNContext denoiseContext;

    public PathTracerPipeline(Vector2i size, in PathTracer.GpuSettings ptSettings)
    {
        pathTracer = new PathTracer(size.X, size.Y, ptSettings);
        SetSize(size);

        PathTracerOutput = OutputTexture.Noisy;
        AutoDenoiseSamplesThreshold = -1; // don't run automatically
    }

    public void Compute()
    {
        pathTracer.Compute();

        if (DenoisingEnabled && pathTracer.AccumulatedSamples == AutoDenoiseSamplesThreshold && !DoDebugBVHTraversal)
        {
            Denoise();
        }
    }

    public unsafe void Denoise()
    {
        if (!OIDN.LibraryFound)
        {
            Logger.Log(Logger.LogLevel.Error, $"Tried to invoke Intel Denoiser but {OIDN.LIBRARY_NAME} was not found");
            return;
        }

        int width = RenderResolution.X;
        int height = RenderResolution.Y;
        int imageSize = width * height * 3 * sizeof(float);

        pathTracer.Result.Download(BBG.Texture.PixelFormat.RGB, BBG.Texture.PixelType.Float, OIDN.GetBufferData(denoiseContext.BeautyBuffer), imageSize);
        pathTracer.AlbedoTexture.Download(BBG.Texture.PixelFormat.RGB, BBG.Texture.PixelType.Float, OIDN.GetBufferData(denoiseContext.AlbedoBuffer), imageSize);
        pathTracer.NormalTexture.Download(BBG.Texture.PixelFormat.RGB, BBG.Texture.PixelType.Float, OIDN.GetBufferData(denoiseContext.NormalBuffer), imageSize);

        if (DoPrefiltering)
        {
            OIDN.ExecuteFilter(denoiseContext.AlbedoFilter);
            OIDN.ExecuteFilter(denoiseContext.NormalFilter);
        }
        OIDN.ExecuteFilter(denoiseContext.BeautyFilter);

        denoisedTexture.Upload2D(width, height, BBG.Texture.PixelFormat.RGB, BBG.Texture.PixelType.Float, OIDN.GetBufferData(denoiseContext.OutputBuffer));

        if (PathTracerOutput == OutputTexture.Noisy)
        {
            PathTracerOutput = OutputTexture.Denoised;
        }
    }

    public void ResetAccumulation()
    {
        if (PathTracerOutput == OutputTexture.Denoised)
        {
            // The denoised view is not live. But reseting accumulation means
            // we likely want to see a live preview so revert to noisy image
            PathTracerOutput = OutputTexture.Noisy;
        }
        pathTracer.ResetAccumulation();
    }

    public unsafe void SetSize(Vector2i size)
    {
        pathTracer.SetSize(size.X, size.Y);

        if (denoisedTexture != null) denoisedTexture.Dispose();
        denoisedTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
        denoisedTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        denoisedTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        denoisedTexture.Allocate(size.X, size.Y, 1, BBG.Texture.InternalFormat.R32G32B32A32Float);
        denoisedTexture.Fill(new Vector4(0.0f));

        // Setup Intel Open Image Denoiser (OIDN)
        if (OIDN.LibraryFound)
        {
            Environment.SetEnvironmentVariable("OIDN_NUM_THREADS", "16");
            if (denoiseContext.Device != null)
            { 
                OIDN.ReleaseBuffer(denoiseContext.BeautyBuffer);
                OIDN.ReleaseBuffer(denoiseContext.AlbedoBuffer);
                OIDN.ReleaseBuffer(denoiseContext.NormalBuffer);
                OIDN.ReleaseBuffer(denoiseContext.OutputBuffer);

                OIDN.ReleaseFilter(denoiseContext.BeautyFilter);
                OIDN.ReleaseFilter(denoiseContext.AlbedoFilter);
                OIDN.ReleaseFilter(denoiseContext.NormalFilter);

                OIDN.ReleaseDevice(denoiseContext.Device);
            }

            denoiseContext.Device = OIDN.NewDevice(OIDN.DeviceType.DEFAULT);
            {
                OIDN.Error error = OIDN.GetDeviceError(denoiseContext.Device, out byte* str);
                if (error != OIDN.Error.NONE)
                {
                    Logger.Log(Logger.LogLevel.Error, $"OIDN: {error} {Marshal.PtrToStringAnsi((nint)str)}");
                }
            }
            OIDN.SetDeviceErrorFunction(denoiseContext.Device, &OIDNContext.ErrorCallback, null);
            //OIDN.SetDeviceInt(denoiseContext.Device, Helper.GetCString("verbose"u8), 0);
            //OIDN.SetDeviceInt(DenoiseContext.Device, Helper.GetCString("numThreads"u8), 16);
            OIDN.CommitDevice(denoiseContext.Device);

            //OIDN.DeviceType deviceType = (OIDN.DeviceType)OIDN.GetDeviceInt(denoiseContext.Device, Helper.GetCString("type"u8));
            //Logger.Log(Logger.LogLevel.Info, $"OIDN: Selected device type is {deviceType}");

            int width = size.X;
            int height = size.Y;
            int imageSize = width * height * 3 * sizeof(float);
            denoiseContext.BeautyBuffer = OIDN.NewBuffer(denoiseContext.Device, imageSize);
            denoiseContext.AlbedoBuffer = OIDN.NewBuffer(denoiseContext.Device, imageSize);
            denoiseContext.NormalBuffer = OIDN.NewBuffer(denoiseContext.Device, imageSize);
            denoiseContext.OutputBuffer = OIDN.NewBuffer(denoiseContext.Device, imageSize);

            denoiseContext.BeautyFilter = OIDN.NewFilter(denoiseContext.Device, Helper.GetCString("RT"u8));
            OIDN.SetFilterImage(denoiseContext.BeautyFilter, Helper.GetCString("color"u8), denoiseContext.BeautyBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.SetFilterImage(denoiseContext.BeautyFilter, Helper.GetCString("albedo"u8), denoiseContext.AlbedoBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.SetFilterImage(denoiseContext.BeautyFilter, Helper.GetCString("normal"u8), denoiseContext.NormalBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.SetFilterImage(denoiseContext.BeautyFilter, Helper.GetCString("output"u8), denoiseContext.OutputBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.SetFilterBool(denoiseContext.BeautyFilter, Helper.GetCString("hdr"u8), true);
            OIDN.SetFilterBool(denoiseContext.BeautyFilter, Helper.GetCString("cleanAux"u8), true);
            OIDN.SetFilterInt(denoiseContext.BeautyFilter, Helper.GetCString("quality"u8), (int)OIDN.Quality.HIGH);
            OIDN.CommitFilter(denoiseContext.BeautyFilter);

            denoiseContext.AlbedoFilter = OIDN.NewFilter(denoiseContext.Device, Helper.GetCString("RT"u8));
            OIDN.SetFilterImage(denoiseContext.AlbedoFilter, Helper.GetCString("albedo"u8), denoiseContext.AlbedoBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.SetFilterImage(denoiseContext.AlbedoFilter, Helper.GetCString("output"u8), denoiseContext.AlbedoBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.CommitFilter(denoiseContext.AlbedoFilter);

            denoiseContext.NormalFilter = OIDN.NewFilter(denoiseContext.Device, Helper.GetCString("RT"u8));
            OIDN.SetFilterImage(denoiseContext.NormalFilter, Helper.GetCString("normal"u8), denoiseContext.NormalBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.SetFilterImage(denoiseContext.NormalFilter, Helper.GetCString("output"u8), denoiseContext.NormalBuffer, OIDN.Format.FLOAT3, width, height, 0, 0, 0);
            OIDN.CommitFilter(denoiseContext.NormalFilter);
        }
    }

    public ref readonly PathTracer.GpuSettings GetPTSettings()
    {
        return ref pathTracer.GetGpuSettings();
    }

    public void Dispose()
    {
        pathTracer.Dispose();
        denoisedTexture.Dispose();
    }
}
