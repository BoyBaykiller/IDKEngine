namespace IDKEngine.GpuTypes
{
    unsafe struct GpuWavefrontPTHeader
    {
        public GpuDispatchCmd DispatchCommand;
        public fixed uint Counts[2];
        public uint PingPongIndex;
        public uint AccumulatedSamples;
    }
}
