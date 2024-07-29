using System;
using System.IO;
using System.Runtime.InteropServices;
using ReFuel.Stb.Native;

namespace IDKEngine
{
    public static unsafe class ImageLoader
    {
        public enum ColorComponents : int
        {
            R,
            RA,
            RGB,
            RGBA,
        }

        public record struct ImageHeader
        {
            public int Width;
            public int Height;
            public int Channels;

            public void SetChannels(ColorComponents colorComponents)
            {
                int channels = colorComponents switch
                {
                    ColorComponents.R => 1,
                    ColorComponents.RA => 2,
                    ColorComponents.RGB => 3,
                    ColorComponents.RGBA => 4,
                    _ => throw new NotSupportedException($"Can not convert {nameof(colorComponents)} = {colorComponents} to {nameof(channels)}"),
                };
                Channels = channels;
            }
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

        public static bool TryGetImageHeader(ReadOnlySpan<byte> imageData, out ImageHeader imageHeader)
        {
            fixed (byte* ptr = imageData)
            {
                return TryGetImageHeader(ptr, imageData.Length, out imageHeader);
            }
        }

        public static bool TryGetImageHeader(byte* data, int size, out ImageHeader imageHeader)
        {
            ImageHeader imageHeaderCopy = new ImageHeader();

            int success = Stbi.info_from_memory(data, size, &imageHeaderCopy.Width, &imageHeaderCopy.Height, &imageHeaderCopy.Channels);

            imageHeader = imageHeaderCopy;
            return success == 1;
        }

        public static string? GetFailureReason()
        {
            sbyte* ptr = Stbi.failure_reason();
            string message = Marshal.PtrToStringAnsi((nint)ptr);

            return message;
        }
    }
}
