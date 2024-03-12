using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuLight
    {
        public Vector3 Position;
        public float Radius;
        public Vector3 Color;
        public int PointShadowIndex;

        private Vector3 _prevPosition;
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

        private readonly float _pad0;

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
