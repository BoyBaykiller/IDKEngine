using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.GpuTypes
{
    record struct GpuPointShadow
    {
        public const int FACE_COUNT = (int)RenderMatrix.Count;

        public enum RenderMatrix : int
        {
            PosX,
            NegX,
            PosY,
            NegY,
            PosZ,
            NegZ,
            Count,
        }

        public ref Matrix4 this[RenderMatrix matrix] => ref Unsafe.AsRef(ref Matrices[(int)matrix]);

        public BBG.Texture.BindlessHandle Texture;
        public BBG.Texture.BindlessHandle ShadowTexture;

        public MatrixArray Matrices;

        public Vector3 Position;
        public float NearPlane;

        public BBG.Texture.BindlessHandle RayTracedShadowTexture;
        public float FarPlane;
        public int LightIndex;

        [InlineArray(FACE_COUNT)]
        public struct MatrixArray
        {
            public Matrix4 _element;
        }
    }
}
