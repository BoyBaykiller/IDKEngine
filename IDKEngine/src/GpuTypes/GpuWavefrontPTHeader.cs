using System.Runtime.CompilerServices;

namespace IDKEngine
{
    [InlineArray(2)]
    public struct GpuDispatchCmd2
    {
        private GpuDispatchCmd _element0;
    }

    unsafe struct GpuWavefrontPTHeader
    {
        public GpuDispatchCmd2 DispatchCommands;
        public fixed uint Counts[2];
        public uint AccumulatedSamples;
    }
}
