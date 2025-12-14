using System;
using System.IO;
using System.Runtime.InteropServices;
using ReFuel.Stb.Native;

namespace IDKEngine;

public static unsafe class ImageLoader
{
    public enum ColorComponents : int
    {
        Default,
        R,
        RA,
        RGB,
        RGBA,
    }

    public record struct ImageHeader
    {
        public readonly int SizeInBytes => Width * Height * Channels * ChannelSizeInBytes;
        public readonly ColorComponents ColorComponents => ChannelsToColorComponents(Channels);

        public int Width;
        public int Height;
        public int Channels;
        public int ChannelSizeInBytes;

        public void SetChannels(ColorComponents colorComponents)
        {
            Channels = ColorComponentsToChannels(colorComponents);
        }

        public static int ColorComponentsToChannels(ColorComponents colorComponents)
        {
            int channels = colorComponents switch
            {
                ColorComponents.Default => 0,
                ColorComponents.R => 1,
                ColorComponents.RA => 2,
                ColorComponents.RGB => 3,
                ColorComponents.RGBA => 4,
                _ => throw new NotSupportedException($"Can not convert {nameof(colorComponents)} = {colorComponents} to {nameof(channels)}"),
            };
            return channels;
        }

        public static ColorComponents ChannelsToColorComponents(int channels)
        {
            ColorComponents colorComponents = channels switch
            {
                1 => ColorComponents.R,
                2 => ColorComponents.RA,
                3 => ColorComponents.RGB,
                4 => ColorComponents.RGBA,
                _ => throw new NotSupportedException($"Can not convert {nameof(channels)} = {channels} to {nameof(colorComponents)}"),
            };
            return colorComponents;
        }
    }

    public struct ImageResult : IDisposable
    {
        public readonly Span<byte> Pixels => new Span<byte>(Memory, Header.SizeInBytes);

        public readonly bool IsLoadedSuccesfully => Memory != null;

        public ImageHeader Header;
        public void* Memory;

        public void Dispose()
        {
            if (IsLoadedSuccesfully)
            {
                Stbi.image_free(Memory);
                Memory = null;
            }
        }
    }

    public static ImageResult Load(string path, ColorComponents colorComponents = ColorComponents.Default, bool flip = false)
    {
        byte[] imageData = File.ReadAllBytes(path);
        return Load(imageData, colorComponents, flip);
    }

    public static ImageResult Load(ReadOnlySpan<byte> imageData, ColorComponents colorComponents = ColorComponents.Default, bool flip = false)
    {
        ImageResult imageResult = new ImageResult();
        fixed (byte* ptr = imageData)
        {
            int desiredChannels = ImageHeader.ColorComponentsToChannels(colorComponents);
            imageResult.Header.ChannelSizeInBytes = GetImageChannelSize(ptr, imageData.Length);

            Stbi.set_flip_vertically_on_load(flip ? 1 : 0);

            if (Stbi.is_hdr_from_memory(ptr, imageData.Length) == 1)
            {
                imageResult.Memory = Stbi.loadf_from_memory(
                    ptr,
                    imageData.Length,
                    &imageResult.Header.Width,
                    &imageResult.Header.Height,
                    &imageResult.Header.Channels,
                    desiredChannels
                );
            }
            else
            {
                imageResult.Memory = Stbi.load_from_memory(
                    ptr,
                    imageData.Length,
                    &imageResult.Header.Width,
                    &imageResult.Header.Height,
                    &imageResult.Header.Channels,
                    desiredChannels
                );
            }

            if (colorComponents != ColorComponents.Default)
            {
                imageResult.Header.Channels = desiredChannels;
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
        imageHeaderCopy.ChannelSizeInBytes = GetImageChannelSize(data, size);

        imageHeader = imageHeaderCopy;
        return success == 1;
    }

    public static string? GetFailureReason()
    {
        sbyte* ptr = Stbi.failure_reason();
        string message = Marshal.PtrToStringAnsi((nint)ptr);

        return message;
    }

    private static int GetImageChannelSize(byte* data, int size)
    {
        if (Stbi.is_hdr_from_memory(data, size) == 1)
        {
            return sizeof(float);
        }
        else
        {
            return sizeof(byte);
        }
    }
}
