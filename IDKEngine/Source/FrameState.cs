using OpenTK.Mathematics;

namespace IDKEngine
{
    public record struct FrameState
    {
        public CameraState CameraState;   
        public float AnimationTime;
    }

    public struct CameraState
    {
        public Vector3 Position;
        public Vector3 UpVector;
        public float LookX;
        public float LookY;
    }
}
