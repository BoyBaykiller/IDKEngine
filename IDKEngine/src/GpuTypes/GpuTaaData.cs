using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GpuTaaData
    {
        public Vector2 Jitter;
        public int Samples;
        public float MipmapBias;
        public TemporalAntiAliasingMode TemporalAntiAliasingMode;
    }
}
