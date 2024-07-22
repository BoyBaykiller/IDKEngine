using System;
using Ktx;

namespace IDKEngine
{
    public unsafe class Ktx2Texture
    {
        private Ktx2.Texture* texture;

        public int BaseWidth => (int)texture->BaseWidth;
        public int BaseHeight => (int)texture->BaseHeight;
        public int BaseDepth => (int)texture->BaseDepth;
        public int Levels => (int)texture->NumLevels;
        public int DataSize => (int)texture->DataSize;
        public byte* Data => texture->PData;
        public Ktx2.SupercmpScheme SupercompressionScheme => texture->SupercompressionScheme;

        public int Channels => (int)Ktx2.GetNumComponents(texture);
        public bool NeedsTranscoding => Ktx2.NeedsTranscoding(texture);
        
        public static Ktx2.ErrorCode FromMemory(ReadOnlySpan<byte> memory, Ktx2.TextureCreateFlag createFlags, out Ktx2Texture ktx2Texture)
        {
            ktx2Texture = new Ktx2Texture();
            return Ktx2.CreateFromMemory(memory[0], (nuint)memory.Length, createFlags, out ktx2Texture.texture);
        }

        public Ktx2.ErrorCode Transcode(Ktx2.TranscodeFormat format, Ktx2.TranscodeFlagBits flags)
        {
            return Ktx2.TranscodeBasis(texture, format, flags);
        }

        public Ktx2.ErrorCode GetImageDataOffset(int level, out nint dataOffset)
        {
            return GetImageDataOffset(level, 0, 0, out dataOffset);
        }

        public Ktx2.ErrorCode GetImageDataOffset(int level, int layer, int faceSlide, out nint dataOffset)
        {
            Ktx2.ErrorCode errorCode = Ktx2.GetImageOffset(texture, (uint)level, (uint)layer, (uint)faceSlide, out nuint dataOffsetLocal);
            dataOffset = (nint)dataOffsetLocal;

            return errorCode;
        }

        public void Dispose()
        {
            Ktx2.Destroy(texture);
        }
    }
}
