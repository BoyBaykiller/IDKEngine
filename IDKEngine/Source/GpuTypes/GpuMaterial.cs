using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using BBOpenGL;

namespace IDKEngine.GpuTypes
{
    public struct GpuMaterial
    {
        public static readonly int TEXTURE_COUNT = Enum.GetValues<BindlessHandle>().Length;

        public enum BindlessHandle : int
        {
            BaseColor,
            MetallicRoughness,
            Normal,
            Emissive,
            Transmission,
        }

        public ref BBG.Texture.BindlessHandle this[BindlessHandle textureType]
        {
            get
            {
                switch (textureType)
                {
                    case BindlessHandle.BaseColor: return ref Unsafe.AsRef(ref BaseColorTexture);
                    case BindlessHandle.MetallicRoughness: return ref Unsafe.AsRef(ref MetallicRoughnessTexture);
                    case BindlessHandle.Normal: return ref Unsafe.AsRef(ref NormalTexture);
                    case BindlessHandle.Emissive: return ref Unsafe.AsRef(ref EmissiveTexture);
                    case BindlessHandle.Transmission: return ref Unsafe.AsRef(ref TransmissionTexture);
                    default: throw new NotSupportedException($"Unsupported {nameof(BindlessHandle)} {textureType}");
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
