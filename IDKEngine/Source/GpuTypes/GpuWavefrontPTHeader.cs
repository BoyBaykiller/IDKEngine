using BBOpenGL;
using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

unsafe struct GpuWavefrontPTHeader
{
    public BBG.DispatchIndirectCommand DispatchCommand;
    public Vector3i RayBoundsMin;
    public Vector3i RayBoundsMax;
    public fixed uint Counts[2];
    public uint PingPongIndex;
    public uint AccumulatedSamples;
}
