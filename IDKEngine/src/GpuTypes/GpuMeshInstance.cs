using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuMeshInstance
    {
        public Matrix4 ModelMatrix
        {
            get => Mat3x4ToMat4x4Tranposed(modelMatrix3x4);

            set
            {
                modelMatrix3x4 = Mat4x4ToMat3x4Transposed(value);
            }
        }
        public Matrix4 InvModelMatrix => Mat3x4ToMat4x4Tranposed(invModelMatrix3x4);
        public Matrix4 PrevModelMatrix => Mat3x4ToMat4x4Tranposed(prevModelMatrix3x4);


        private Matrix3x4 _modelMatrixPacked;
        private Matrix3x4 modelMatrix3x4
        {
            get => _modelMatrixPacked;

            set
            {
                if (_modelMatrixPacked == Matrix3x4.Zero)
                {
                    _modelMatrixPacked = value;
                }

                prevModelMatrix3x4 = _modelMatrixPacked;
                _modelMatrixPacked = value;

                invModelMatrix3x4 = Matrix3x4.Invert(_modelMatrixPacked);
            }
        }

        private Matrix3x4 invModelMatrix3x4;
        private Matrix3x4 prevModelMatrix3x4;

        public void SetPrevToCurrentMatrix()
        {
            prevModelMatrix3x4 = modelMatrix3x4;
        }

        public bool DidMove()
        {
            return PrevModelMatrix != ModelMatrix;
        }

        public static Matrix3x4 Mat4x4ToMat3x4Transposed(in Matrix4 model)
        {
            Matrix4x3 newAss = new Matrix4x3(
                model.Row0.Xyz,
                model.Row1.Xyz,
                model.Row2.Xyz,
                model.Row3.Xyz
            );

            Matrix3x4 result = Matrix4x3.Transpose(newAss);

            return result;
        }

        public static Matrix4 Mat3x4ToMat4x4Tranposed(in Matrix3x4 model)
        {
            Matrix4x3 tranposed = Matrix3x4.Transpose(model);

            Matrix4 result = new Matrix4(
                new Vector4(tranposed.Row0, 0.0f),
                new Vector4(tranposed.Row1, 0.0f),
                new Vector4(tranposed.Row2, 0.0f),
                new Vector4(tranposed.Row3, 1.0f)
            );

            return result;
        }
    }
}
