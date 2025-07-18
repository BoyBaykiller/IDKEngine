﻿using OpenTK.Mathematics;
using IDKEngine.Render;

namespace IDKEngine.GpuTypes;

record struct GpuTaaData
{
    public Vector2 Jitter;
    public int SampleCount;
    public float MipmapBias;
    public RasterPipeline.AntiAliasingMode TemporalAntiAliasingMode;
}
