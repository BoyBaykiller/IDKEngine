using OpenTK.Mathematics;
using IDKEngine.Utils;

namespace IDKEngine.GpuTypes
{
    public record struct GpuMeshInstance
    {
        // In a typical 4x4 matrix the W component is useless:
        // (x, y, z, 0.0)
        // (x, y, z, 0.0)
        // (x, y, z, 0.0)
        // (x, y, z, 1.0)
        // We can't store Matrix4x3 as GLSL-std430 requires vec4 alignment (vec3s would add padding). For that reason we store a Matrix3x4.
        // We get this Matrix3x4 by taking the Matrix4x4, constructing a Matrix4x3 by removing the useless W, and then transposing. In GLSL we then specify row_major.

        public Matrix4 ModelMatrix
        {
            get => MyMath.Matrix3x4ToTransposed4x4(ModelMatrix3x4);

            set
            {
                ModelMatrix3x4 = MyMath.Matrix4x4ToTranposed3x4(value);
            }
        }
        
        public Matrix4 InvModelMatrix => MyMath.Matrix3x4ToTransposed4x4(invModelMatrix3x4);
        
        public Matrix4 PrevModelMatrix => MyMath.Matrix3x4ToTransposed4x4(prevModelMatrix3x4);

        public Matrix3x4 ModelMatrix3x4
        {
            get => modelMatrix3x4;

            private set
            {
                modelMatrix3x4 = value;
                invModelMatrix3x4 = Matrix3x4.Invert(ModelMatrix3x4);
            }
        }

        private Matrix3x4 modelMatrix3x4;
        private Matrix3x4 invModelMatrix3x4;
        private Matrix3x4 prevModelMatrix3x4;

        public int MeshIndex;
        private readonly float _pad0;
        private readonly float _pad1;
        private readonly float _pad2;

        public bool DidMove()
        {
            return prevModelMatrix3x4 != modelMatrix3x4;
        }

        public void SetPrevToCurrentMatrix()
        {
            prevModelMatrix3x4 = ModelMatrix3x4;
        }
    }
}
