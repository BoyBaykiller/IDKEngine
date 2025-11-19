using BBOpenGL;
using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

unsafe struct GpuWavefrontPTHeader
{
    public BBG.DispatchIndirectCommand DispatchCommand;
    public Vector3i RayBoundsMin0;
    public Vector3i RayBoundsMin1;
    public Vector3i RayBoundsMax0;
    public Vector3i RayBoundsMax1;
    public fixed uint Counts[2];
    public uint PingPongIndex;
    public uint AccumulatedSamples;
}
