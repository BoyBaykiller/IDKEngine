using System.Runtime.InteropServices;
using OpenTK.Mathematics;

namespace IDKEngine;

// Don't change order! Used in binary serialization

[StructLayout(LayoutKind.Sequential, Pack = 0, Size = 512)]
public record struct FrameState
{
    public CameraState CameraState;
    public float AnimationTime;
}

public record struct CameraState
{
    public Vector3 Position;
    public float LookX;

    public Vector3 UpVector;
    public float LookY;

    public float FovY;
}
