using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct GpuMeshInstance
    {
        private Matrix4 _modelMatrix;
        public Matrix4 ModelMatrix
        {
            get => _modelMatrix;

            set
            {
                if (_modelMatrix == Matrix4.Zero)
                {
                    _modelMatrix = value;
                }

                PrevModelMatrix = _modelMatrix;
                _modelMatrix = value;
                InvModelMatrix = Matrix4.Invert(_modelMatrix);
            }
        }

        public Matrix4 InvModelMatrix { get; private set; }
        public Matrix4 PrevModelMatrix { get; private set; }

        public void SetPrevToCurrentMatrix()
        {
            PrevModelMatrix = ModelMatrix;
        }

        public bool DidMove()
        {
            return PrevModelMatrix != ModelMatrix;
        }
    }
}
