using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public record struct GpuLight
    {
        public Vector3 PrevPosition
        {
            get
            {
                if (_prevPosition == new Vector3(0.0f))
                {
                    _prevPosition = Position;
                }

                return _prevPosition;
            }

            set => _prevPosition = value;
        }

        public Vector3 Position;
        public float Radius;

        public Vector3 Color;
        public int PointShadowIndex = -1;

        private Vector3 _prevPosition;
        private readonly float _pad0;


        public GpuLight()
        {
        }

        public bool DidMove()
        {
            return Position != PrevPosition;
        }

        public void SetPrevToCurrentPosition()
        {
            PrevPosition = Position;
        }
    }
}
