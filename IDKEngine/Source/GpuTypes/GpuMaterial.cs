using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.GpuTypes
{
    public record struct GpuMaterial
    {
        public static readonly int TEXTURE_COUNT = Enum.GetValues<TextureType>().Length;

        public enum TextureType : int
        {
            BaseColor,
            MetallicRoughness,
            Normal,
            Emissive,
            Transmission,
        }

        public ref BBG.Texture.BindlessHandle this[TextureType textureType]
        {
            get
            {
                switch (textureType)
                {
                    case TextureType.BaseColor: return ref Unsafe.AsRef(ref BaseColorTexture);
                    case TextureType.MetallicRoughness: return ref Unsafe.AsRef(ref MetallicRoughnessTexture);
                    case TextureType.Normal: return ref Unsafe.AsRef(ref NormalTexture);
                    case TextureType.Emissive: return ref Unsafe.AsRef(ref EmissiveTexture);
                    case TextureType.Transmission: return ref Unsafe.AsRef(ref TransmissionTexture);
                    default: throw new NotSupportedException($"Unsupported {nameof(TextureType)} {textureType}");
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

        public BBG.Texture.BindlessHandle BaseColorTexture;
        public BBG.Texture.BindlessHandle MetallicRoughnessTexture;

        public BBG.Texture.BindlessHandle NormalTexture;
        public BBG.Texture.BindlessHandle EmissiveTexture;

        public BBG.Texture.BindlessHandle TransmissionTexture;
        public bool DoAlphaBlending;
        private readonly uint _pad0;
    }
}
