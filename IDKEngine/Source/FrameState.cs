using OpenTK.Mathematics;

namespace IDKEngine
{
    public record struct FrameState
    {
        public Vector3 Position;
        public Vector3 UpVector;
        public float LookX;
        public float LookY;
    }
}
