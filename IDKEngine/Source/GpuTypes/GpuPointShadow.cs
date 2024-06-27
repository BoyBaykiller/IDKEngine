using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using BBOpenGL;

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
                    case RenderMatrix.PosX: return ref Unsafe.AsRef(ref PosX);
                    case RenderMatrix.NegX: return ref Unsafe.AsRef(ref NegX);
                    case RenderMatrix.PosY: return ref Unsafe.AsRef(ref PosY);
                    case RenderMatrix.NegY: return ref Unsafe.AsRef(ref NegY);
                    case RenderMatrix.PosZ: return ref Unsafe.AsRef(ref PosZ);
                    case RenderMatrix.NegZ: return ref Unsafe.AsRef(ref NegZ);
                    default: throw new NotSupportedException($"Unsupported {nameof(RenderMatrix)} {matrix}");
                }
            }
        }

        public BBG.Texture.BindlessHandle Texture;
        public BBG.Texture.BindlessHandle ShadowTexture;

        public Matrix4 PosX;
        public Matrix4 NegX;
        public Matrix4 PosY;
        public Matrix4 NegY;
        public Matrix4 PosZ;
        public Matrix4 NegZ;

        public Vector3 Position;
        public float NearPlane;

        public BBG.Texture.BindlessHandle RayTracedShadowTexture;
        public float FarPlane;
        public int LightIndex;
    }
}
