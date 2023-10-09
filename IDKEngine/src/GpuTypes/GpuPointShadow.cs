using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GpuPointShadow
    {
        public enum Matrix : int
        {
            PosX,
            NegX,
            PosY,
            NegY,
            PosZ,
            NegZ,
        }
        public ref Matrix4 this[Matrix matrix]
        {
            get
            {
                switch (matrix)
                {
                    case Matrix.PosX: return ref Unsafe.AsRef(PosX);
                    case Matrix.NegX: return ref Unsafe.AsRef(NegX);
                    case Matrix.PosY: return ref Unsafe.AsRef(PosY);
                    case Matrix.NegY: return ref Unsafe.AsRef(NegY);
                    case Matrix.PosZ: return ref Unsafe.AsRef(PosZ);
                    case Matrix.NegZ: return ref Unsafe.AsRef(NegZ);
                    default: throw new NotSupportedException($"Unsupported {nameof(Matrix)} {matrix}");
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
