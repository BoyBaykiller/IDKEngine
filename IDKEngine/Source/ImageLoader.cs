using System;
using System.IO;
using System.Runtime.InteropServices;
using ReFuel.Stb;

namespace IDKEngine
{
    public static unsafe class ImageLoader
    {
        public struct ImageHeader
        {
            public int Width;
            public int Height;
            public int Channels;
        }

        public struct ImageResult : IDisposable
        {
            public int SizeInBytes => Header.Width * Header.Height * Channels;

            public Span<byte> Pixels => new Span<byte>(RawPixels, SizeInBytes);

            public ImageHeader Header;
            public int Channels;
            public byte* RawPixels;

            public void Dispose()
            {
                if (RawPixels != null)
                {
                    Stbi.image_free(RawPixels);
                    RawPixels = null;
                }
            }
        }

        public static ImageResult Load(string path, int desiredChannels = 0)
        {
            byte[] imageData = File.ReadAllBytes(path);
            return Load(imageData, desiredChannels);
        }

        public static ImageResult Load(ReadOnlySpan<byte> imageData, int desiredChannels = 0)
        {
            ImageResult imageResult = new ImageResult();
            fixed (byte* ptr = imageData)
            {
                imageResult.RawPixels = Stbi.load_from_memory(
                    ptr,
                    imageData.Length,
                    &imageResult.Header.Width,
                    &imageResult.Header.Height,
                    &imageResult.Header.Channels,
                    desiredChannels
                );

                imageResult.Channels = desiredChannels;
                if (imageResult.Channels <= 0 || imageResult.Channels > 4)
                {
                    imageResult.Channels = imageResult.Header.Channels;
                }
            }

            return imageResult;
        }

        public static ImageHeader GetImageHeader(ReadOnlySpan<byte> imageData)
        {
            fixed (byte* ptr = imageData)
            {
                return GetImageHeader(ptr, imageData.Length);
            }
        }

        public static ImageHeader GetImageHeader(byte* data, int size)
        {
            ImageHeader imageHeader = new ImageHeader();

            Stbi.info_from_memory(data, size, &imageHeader.Width, &imageHeader.Height, &imageHeader.Channels);

            return imageHeader;
        }

        public static string? GetFailureReason()
        {
            sbyte* ptr = Stbi.failure_reason();
            string message = Marshal.PtrToStringAnsi((nint)ptr);

            return message;
        }
    }
}
