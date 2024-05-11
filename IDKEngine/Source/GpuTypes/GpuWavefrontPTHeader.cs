using BBOpenGL;

namespace IDKEngine.GpuTypes
{
    unsafe struct GpuWavefrontPTHeader
    {
        public BBG.DispatchIndirectCommand DispatchCommand;
        public fixed uint Counts[2];
        public uint PingPongIndex;
        public uint AccumulatedSamples;
    }
}
