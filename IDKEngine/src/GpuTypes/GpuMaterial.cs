using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuMaterial
    {
        public enum TextureHandle : int
        {
            BaseColor,
            MetallicRoughness,
            Normal,
            Emissive,
            Transmission,
        }

        public ref ulong this[TextureHandle textureType]
        {
            get
            {
                switch (textureType)
                {
                    case TextureHandle.BaseColor: return ref Unsafe.AsRef(BaseColorTextureHandle);
                    case TextureHandle.MetallicRoughness: return ref Unsafe.AsRef(MetallicRoughnessTextureHandle);
                    case TextureHandle.Normal: return ref Unsafe.AsRef(NormalTextureHandle);
                    case TextureHandle.Emissive: return ref Unsafe.AsRef(EmissiveTextureHandle);
                    case TextureHandle.Transmission: return ref Unsafe.AsRef(TransmissionTextureHandle);
                    default: throw new NotSupportedException($"Unsupported {nameof(TextureHandle)} {textureType}");
                }
            }
        }

        public Vector3 EmissiveFactor;
        public uint BaseColorFactor;

        public float TransmissionFactor;
        public float AlphaCutoff;
        public float RoughnessFactor;
        public float MetallicFactor;

        public Vector3 Absorbance;
        public float IOR;

        public ulong TransmissionTextureHandle;
        private readonly ulong _pad0;

        public ulong BaseColorTextureHandle;
        public ulong MetallicRoughnessTextureHandle;

        public ulong NormalTextureHandle;
        public ulong EmissiveTextureHandle;
    }
}
