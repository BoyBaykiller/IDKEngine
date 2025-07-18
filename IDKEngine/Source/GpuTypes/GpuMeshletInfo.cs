﻿using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes;

public record struct GpuMeshletInfo
{
    public Vector3 Min;
    private readonly float _pad0;

    public Vector3 Max;
    private readonly float _pad1;
}
