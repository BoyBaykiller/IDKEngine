using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.GpuTypes;

namespace IDKEngine.Render;

class PathTracer : IDisposable
{
    public int SamplesPerPixel = 1;

    private int cachedRayDepth;

    private int _rayDepth;
    public int RayDepth
    {
        get => _rayDepth;
        set
        {
            _rayDepth = value;
            ResetAccumulation();
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

    public float FocalLength
    {
        get => settings.FocalLength;

        set
        {
            settings.FocalLength = value;
            ResetAccumulation();
        }
    }

    public float LenseRadius
    {
        get => settings.LenseRadius;

        set
        {
            settings.LenseRadius = value;
            ResetAccumulation();
        }
    }

    public bool DoDebugBVHTraversal
    {
        get => settings.DoDebugBVHTraversal == 1;

        set
        {
            settings.DoDebugBVHTraversal = value ? 1 : 0;

            if (value)
            {
                cachedRayDepth = RayDepth;
                RayDepth = 1;
            }
            else
            {
                RayDepth = cachedRayDepth;
            }
            ResetAccumulation();
        }
    }

    public bool DoTraceLights
    {
        get => settings.DoTraceLights == 1;

        set
        {
            settings.DoTraceLights = value ? 1 : 0;
            ResetAccumulation();
        }
    }

    public bool DoRussianRoulette
    {
        get => settings.DoRussianRoulette == 1;

        set
        {
            settings.DoRussianRoulette = value ? 1 : 0;
        }
    }

    private bool _doRaySorting;
    public bool DoRaySorting
    {
        get => _doRaySorting;

        set
        {
            _doRaySorting = value;
            BBG.AbstractShaderProgram.SetShaderInsertionValue("PATH_TRACER_DO_RAY_SORTING", DoRaySorting);
        }
    }

    private bool _outputAOVs;
    public bool OutputAOVs
    {
        get => _outputAOVs;

        set
        {
            _outputAOVs = value;
            BBG.AbstractShaderProgram.SetShaderInsertionValue("PATH_TRACER_OUTPUT_AOVS", OutputAOVs);
        }
    }

    public record struct GpuSettings
    {
        public float FocalLength = 8.0f;
        public float LenseRadius = 0.0f;
        public int DoDebugBVHTraversal = 0;
        public int DoTraceLights = 0;
        public int DoRussianRoulette = 1;

        public GpuSettings()
        {
        }
    }

    private GpuSettings settings;

    // Wavefront Path Tracing
    public BBG.Texture Result;
    private readonly BBG.AbstractShaderProgram firstHitProgram;
    private readonly BBG.AbstractShaderProgram nHitProgram;
    private readonly BBG.AbstractShaderProgram finalDrawProgram;

    private BBG.TypedBuffer<GpuWavefrontRay> wavefrontRayBuffer;
    private BBG.TypedBuffer<GpuAovRay> aovRayBuffer;
    private BBG.Buffer wavefrontPTBuffer;
    
    // Ray Sorting
    private const int GROUP_WISE_PROGRAM_STEPS = 9; // Keep in sync between shader and client code!
    private const int DOWN_UP_SWEEP_PROGRAM_STEPS = 7; // Keep in sync between shader and client code!
    private const int PREFIX_SUM_CAPACITY = 1 << (DOWN_UP_SWEEP_PROGRAM_STEPS + GROUP_WISE_PROGRAM_STEPS);

    private readonly BBG.AbstractShaderProgram reorderProgram;
    private readonly BBG.AbstractShaderProgram downUpSweepProgram;
    private readonly BBG.AbstractShaderProgram groupWiseScanProgram;

    private BBG.TypedBuffer<uint> sortedRayIndicesBuffer;
    private BBG.TypedBuffer<uint> cachedKeyBuffer;
    private readonly BBG.TypedBuffer<uint> workGroupSumsPrefixSumBuffer;
    private readonly BBG.TypedBuffer<uint> workGroupPrefixSumBuffer;

    // AOVs
    public BBG.Texture AlbedoTexture;
    public BBG.Texture NormalTexture;

    public PathTracer(int width, int height, GpuSettings settings)
    {
        this.settings = settings;
        DoRaySorting = false;
        OutputAOVs = false;

        // Wavefront Path Tracing
        firstHitProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "PathTracing/FirstHit/compute.glsl"));
        nHitProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "PathTracing/NHit/compute.glsl"));
        finalDrawProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "PathTracing/FinalDraw/compute.glsl"));

        wavefrontRayBuffer = new BBG.TypedBuffer<GpuWavefrontRay>();
        wavefrontRayBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 30);

        aovRayBuffer = new BBG.TypedBuffer<GpuAovRay>();
        aovRayBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 31);

        wavefrontPTBuffer = new BBG.Buffer();
        wavefrontPTBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 32);

        // Ray Sorting
        reorderProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "PathTracing/CountingSort/Reorder/compute.glsl"));
        downUpSweepProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "PathTracing/CountingSort/BlellochScan/DownUpSweep/compute.glsl"));
        groupWiseScanProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "PathTracing/CountingSort/BlellochScan/GroupWise/compute.glsl"));

        sortedRayIndicesBuffer = new BBG.TypedBuffer<uint>();
        sortedRayIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 33);

        cachedKeyBuffer = new BBG.TypedBuffer<uint>();
        cachedKeyBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 34);

        workGroupPrefixSumBuffer = new BBG.TypedBuffer<uint>();
        workGroupPrefixSumBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, PREFIX_SUM_CAPACITY);
        workGroupPrefixSumBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 35);

        workGroupSumsPrefixSumBuffer = new BBG.TypedBuffer<uint>();
        workGroupSumsPrefixSumBuffer.AllocateElements(BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, MyMath.DivUp(PREFIX_SUM_CAPACITY, 1 << GROUP_WISE_PROGRAM_STEPS));
        workGroupSumsPrefixSumBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 36);

        SetSize(width, height);

        RayDepth = 7;
    }

    public void Compute()
    {
        BBG.Cmd.SetUniforms(settings);

        for (int i = 0; i < SamplesPerPixel; i++)
        {
            BBG.Computing.Compute("PathTrace Primary Rays", () =>
            {
                BBG.Cmd.BindImageUnit(Result, 0);
                BBG.Cmd.UseShaderProgram(firstHitProgram);
                BBG.Computing.Dispatch(MyMath.DivUp(Result.Width, 8), MyMath.DivUp(Result.Height, 8), 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit | BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
            });

            for (int j = 1; j < RayDepth; j++)
            {
                int pingPongIndex = j % 2;

                if (DoRaySorting)
                {
                    if (j > 1)
                    {
                        RaySorting();
                    }

                    // Clear the buffer so we can build the inital histogram of sorting keys
                    workGroupPrefixSumBuffer.Fill(0);
                }

                BBG.Computing.Compute($"PathTrace Ray bounce {j}", () =>
                {
                    nint pingPongIndexOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.PingPongIndex));
                    wavefrontPTBuffer.UploadData(pingPongIndexOffset, sizeof(uint), pingPongIndex);

                    nint rayCountsBaseOffset = Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.Counts));
                    wavefrontPTBuffer.UploadData(rayCountsBaseOffset + (1 - pingPongIndex) * sizeof(uint), sizeof(uint), 0);

                    BBG.Cmd.UseShaderProgram(nHitProgram);
                    BBG.Computing.DispatchIndirect(wavefrontPTBuffer, Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.DispatchCommand)));
                    BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit | BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
                });
            }

            BBG.Computing.Compute("Accumulate and output rays color", () =>
            {
                BBG.Cmd.BindImageUnit(Result, 0);
                if (OutputAOVs)
                {
                    BBG.Cmd.BindImageUnit(AlbedoTexture, 1);
                    BBG.Cmd.BindImageUnit(NormalTexture, 2);
                }
                BBG.Cmd.UseShaderProgram(finalDrawProgram);
                BBG.Computing.Dispatch(MyMath.DivUp(Result.Width, 8), MyMath.DivUp(Result.Height, 8), 1);
                BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.TextureFetchBarrierBit | BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit | BBG.Cmd.MemoryBarrierMask.CommandBarrierBit);
            });
            AccumulatedSamples++;
        }
    }

    public unsafe void RaySorting()
    {
        BBG.Computing.Compute("Group wise Prefix Scan", () =>
        {
            BBG.Cmd.UseShaderProgram(groupWiseScanProgram);
            BBG.Computing.Dispatch(workGroupSumsPrefixSumBuffer.NumElements, 1, 1);
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);
        });

        BBG.Computing.Compute($"Blelloch Scan Down+Up Sweep over Groups", () =>
        {
            BBG.Cmd.UseShaderProgram(downUpSweepProgram);
            BBG.Computing.Dispatch(1, 1, 1);
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);
        });

        BBG.Computing.Compute("Reorder items", () =>
        {
            BBG.Cmd.UseShaderProgram(reorderProgram);
            BBG.Computing.DispatchIndirect(wavefrontPTBuffer, Marshal.OffsetOf<GpuWavefrontPTHeader>(nameof(GpuWavefrontPTHeader.DispatchCommand)));
            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);
        });

        sortedRayIndicesBuffer.CopyTo(wavefrontPTBuffer, 0, sizeof(GpuWavefrontPTHeader), sortedRayIndicesBuffer.Size);
    }

    public unsafe void SetSize(int width, int height)
    {
        if (Result != null) Result.Dispose();
        Result = new BBG.Texture(BBG.Texture.Type.Texture2D);
        Result.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        Result.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        Result.Allocate(width, height, 1, BBG.Texture.InternalFormat.R32G32B32A32Float);
        Result.Fill(new Vector4(0.0f));

        if (AlbedoTexture != null) AlbedoTexture.Dispose();
        AlbedoTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
        AlbedoTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        AlbedoTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        AlbedoTexture.Allocate(width, height, 1, BBG.Texture.InternalFormat.R32G32B32A32Float);

        if (NormalTexture != null) NormalTexture.Dispose();
        NormalTexture = new BBG.Texture(BBG.Texture.Type.Texture2D);
        NormalTexture.SetFilter(BBG.Sampler.MinFilter.Linear, BBG.Sampler.MagFilter.Linear);
        NormalTexture.SetWrapMode(BBG.Sampler.WrapMode.ClampToEdge, BBG.Sampler.WrapMode.ClampToEdge);
        NormalTexture.Allocate(width, height, 1, BBG.Texture.InternalFormat.R32G32B32A32Float);

        BBG.Buffer.Recreate(ref wavefrontRayBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, width * height);
        BBG.Buffer.Recreate(ref aovRayBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, width * height);
        BBG.Buffer.Recreate(ref wavefrontPTBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, sizeof(GpuWavefrontPTHeader) + (width * height * sizeof(uint)));
        wavefrontPTBuffer.UploadData(0, sizeof(GpuWavefrontPTHeader), new GpuWavefrontPTHeader()
        {
            DispatchCommand = new BBG.DispatchIndirectCommand() { NumGroupsX = 0, NumGroupsY = 1, NumGroupsZ = 1 },
        });

        BBG.Buffer.Recreate(ref cachedKeyBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, width * height);
        BBG.Buffer.Recreate(ref sortedRayIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, width * height);

        ResetAccumulation();
    }

    public void ResetAccumulation()
    {
        AccumulatedSamples = 0;
    }

    public ref readonly GpuSettings GetGpuSettings()
    {
        return ref settings;
    }

    public void Dispose()
    {
        Result.Dispose();
        AlbedoTexture.Dispose();
        NormalTexture.Dispose();

        firstHitProgram.Dispose();
        nHitProgram.Dispose();
        finalDrawProgram.Dispose();
        reorderProgram.Dispose();
        downUpSweepProgram.Dispose();
        groupWiseScanProgram.Dispose();

        wavefrontRayBuffer.Dispose();
        aovRayBuffer.Dispose();
        wavefrontPTBuffer.Dispose();
        workGroupSumsPrefixSumBuffer.Dispose();
        workGroupPrefixSumBuffer.Dispose();
        sortedRayIndicesBuffer.Dispose();
        cachedKeyBuffer.Dispose();
    }
}
