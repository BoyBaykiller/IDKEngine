using OpenTK.Mathematics;
using IDKEngine.Render;

namespace IDKEngine
{
    struct GpuTaaData
    {
        public Vector2 Jitter;
        public int Samples;
        public float MipmapBias;
        public RasterPipeline.TemporalAntiAliasingMode TemporalAntiAliasingMode;
    }
}
