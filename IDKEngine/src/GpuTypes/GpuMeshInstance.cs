using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuMeshInstance
    {
        // In a typical 4x4 matrix the W component is useless:
        // (x, y, z, 0.0)
        // (x, y, z, 0.0)
        // (x, y, z, 0.0)
        // (x, y, z, 1.0)
        // We can't store Matrix4x3 as GLSL-std430 requires vec4 alignment (vec3s would add padding). For that reason we store a Matrix3x4.
        // We get this Matrix3x4 by taking the Matrix4x4, constructing a Matrix4x3 by removing the useless W and then transposing.

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
                _modelMatrixPacked = value;
                invModelMatrix3x4 = Matrix3x4.Invert(_modelMatrixPacked);
            }
        }

        private Matrix3x4 invModelMatrix3x4;
        private Matrix3x4 prevModelMatrix3x4;

        private readonly Vector3 _pad0;
        public int MeshIndex;

        public void SetPrevToCurrentMatrix()
        {
            prevModelMatrix3x4 = modelMatrix3x4;
        }

        public bool DidMove()
        {
            return modelMatrix3x4 != prevModelMatrix3x4;
        }

        public static Matrix3x4 Mat4x4ToMat3x4Transposed(in Matrix4 model)
        {
            Matrix4x3 fourByThree = new Matrix4x3(
                model.Row0.Xyz,
                model.Row1.Xyz,
                model.Row2.Xyz,
                model.Row3.Xyz
            );

            Matrix3x4 result = Matrix4x3.Transpose(fourByThree);

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
