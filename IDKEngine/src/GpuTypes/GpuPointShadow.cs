using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    struct GpuPointShadow
    {
        public enum RenderMatrix : int
        {
            PosX,
            NegX,
            PosY,
            NegY,
            PosZ,
            NegZ,
        }
        public ref Matrix4 this[RenderMatrix matrix]
        {
            get
            {
                switch (matrix)
                {
                    case RenderMatrix.PosX: return ref Unsafe.AsRef(PosX);
                    case RenderMatrix.NegX: return ref Unsafe.AsRef(NegX);
                    case RenderMatrix.PosY: return ref Unsafe.AsRef(PosY);
                    case RenderMatrix.NegY: return ref Unsafe.AsRef(NegY);
                    case RenderMatrix.PosZ: return ref Unsafe.AsRef(PosZ);
                    case RenderMatrix.NegZ: return ref Unsafe.AsRef(NegZ);
                    default: throw new NotSupportedException($"Unsupported {nameof(RenderMatrix)} {matrix}");
                }
            }
        }

        public ulong Texture;
        public ulong ShadowTexture;

        public Matrix4 PosX;
        public Matrix4 NegX;
        public Matrix4 PosY;
        public Matrix4 NegY;
        public Matrix4 PosZ;
        public Matrix4 NegZ;


        public Vector3 Position;
        public float NearPlane;

        private readonly Vector3 _pad0;
        public float FarPlane;
    }
}
