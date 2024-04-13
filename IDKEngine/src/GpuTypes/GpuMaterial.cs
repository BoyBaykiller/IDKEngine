using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    public struct GpuMaterial
    {
        public static readonly int TEXTURE_COUNT = Enum.GetValues<TextureHandle>().Length;

        public enum TextureHandle : int
        {
            BaseColor,
            MetallicRoughness,
            Normal,
            Emissive,
            Transmission,
        }

        public ref long this[TextureHandle textureType]
        {
            get
            {
                switch (textureType)
                {
                    case TextureHandle.BaseColor: return ref Unsafe.AsRef(ref BaseColorTextureHandle);
                    case TextureHandle.MetallicRoughness: return ref Unsafe.AsRef(ref MetallicRoughnessTextureHandle);
                    case TextureHandle.Normal: return ref Unsafe.AsRef(ref NormalTextureHandle);
                    case TextureHandle.Emissive: return ref Unsafe.AsRef(ref EmissiveTextureHandle);
                    case TextureHandle.Transmission: return ref Unsafe.AsRef(ref TransmissionTextureHandle);
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

        public long BaseColorTextureHandle;
        public long MetallicRoughnessTextureHandle;

        public long NormalTextureHandle;
        public long EmissiveTextureHandle;

        public long TransmissionTextureHandle;
        private readonly long _pad0;
    }
}
