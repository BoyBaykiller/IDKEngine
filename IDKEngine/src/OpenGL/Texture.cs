using System;
using System.Diagnostics;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;

namespace IDKEngine.OpenGL
{
    class Texture : IDisposable
    {
        public enum Type : int
        {
            Cubemap = TextureTarget2d.TextureCubeMap,
            Texture2D = TextureTarget2d.Texture2D,
            Texture3D = TextureTarget.Texture3D,
        }
        public enum InternalFormat : int
        {
            // UNorm = [0, 1]  range
            // SNorm = [-1, 1] range
            // Float = float range
            // Uint  = uint range
            // D     = depth formats

            R8Unorm = SizedInternalFormat.R8,
            R8Uint = SizedInternalFormat.R8ui,

            R16Float = SizedInternalFormat.R16f,

            R32Float = SizedInternalFormat.R32f,
            R32Uint = SizedInternalFormat.R32ui,

            R16G16Float = SizedInternalFormat.Rg16f,

            R11G11B10Float = SizedInternalFormat.R11fG11fB10f,

            R8G8B8A8Unorm = SizedInternalFormat.Rgba8,
            R8G8B8A8Snorm = SizedInternalFormat.Rgba8Snorm,
            R8G8B8A8Srgb = SizedInternalFormat.Srgb8Alpha8,

            R8G8B8SRgba = SizedInternalFormat.Srgb8,
            R16G16B16A16Float = SizedInternalFormat.Rgba16f,
            R32G32B32A32Float = SizedInternalFormat.Rgba32f,

            BC1RgbUnorm = SizedInternalFormat.CompressedRgbS3tcDxt1Ext,
            BC4RUnorm = SizedInternalFormat.CompressedRedRgtc1,
            BC5RgUnorm = SizedInternalFormat.CompressedRgRgtc2,
            BC7RgbaUnorm = SizedInternalFormat.CompressedRgbaBptcUnorm,
            BC7RgbaSrgb = SizedInternalFormat.CompressedSrgbAlphaBptcUnorm,

            // Require GL_KHR_texture_compression_astc
            Astc4X4Rgba = SizedInternalFormat.CompressedRgbaAstc4X4,
            Astc4X4RgbaSrgb = SizedInternalFormat.CompressedSrgb8Alpha8Astc4X4,

            D16Unorm = SizedInternalFormat.DepthComponent16,
            D32Float = SizedInternalFormat.DepthComponent32f,
        }

        public readonly int ID;
        public readonly Type TextureType;
        public InternalFormat TextureFormat { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }
        public int Levels { get; private set; }

        private readonly List<long> associatedTextureHandles = new List<long>(0);
        private readonly List<long> associatedImageHandles = new List<long>(0);

        public Texture(Type textureType)
        {
            GL.CreateTextures((TextureTarget)textureType, 1, out ID);
            TextureType = textureType;
        }

        public void SetFilter(TextureMinFilter minFilter, TextureMagFilter magFilter)
        {
            /// GL_NEAREST_MIPMAP_NEAREST: takes the nearest mipmap to match the pixel size and uses nearest neighbor interpolation for texture sampling.
            /// GL_LINEAR_MIPMAP_NEAREST: takes the nearest mipmap level and samples that level using linear interpolation.
            /// GL_NEAREST_MIPMAP_LINEAR: linearly interpolates between the two mipmaps that most closely match the size of a pixel and samples the interpolated level via nearest neighbor interpolation.
            /// GL_LINEAR_MIPMAP_LINEAR: linearly interpolates between the two closest mipmaps and samples the interpolated level via linear interpolation.

            GL.TextureParameter(ID, TextureParameterName.TextureMinFilter, (int)minFilter);
            GL.TextureParameter(ID, TextureParameterName.TextureMagFilter, (int)magFilter);
        }

        public void SetWrapMode(TextureWrapMode wrapS, TextureWrapMode wrapT)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureWrapS, (int)wrapS);
            GL.TextureParameter(ID, TextureParameterName.TextureWrapT, (int)wrapT);
        }

        public void SetWrapMode(TextureWrapMode wrapS, TextureWrapMode wrapT, TextureWrapMode wrapR)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureWrapS, (int)wrapS);
            GL.TextureParameter(ID, TextureParameterName.TextureWrapT, (int)wrapT);
            GL.TextureParameter(ID, TextureParameterName.TextureWrapR, (int)wrapR);
        }

        public void SetSwizzleR(TextureSwizzle swizzle)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureSwizzleR, (int)swizzle);
        }
        public void SetSwizzleG(TextureSwizzle swizzle)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureSwizzleG, (int)swizzle);
        }
        public void SetSwizzleB(TextureSwizzle swizzle)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureSwizzleB, (int)swizzle);
        }
        public void SetSwizzleA(TextureSwizzle swizzle)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureSwizzleA, (int)swizzle);
        }

        public void SetAnisotropy(float value)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureMaxAnisotropy, value);
        }

        public unsafe void SetBorderColor(Vector4 color)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureBorderColor, &color.X);
        }

        public void SetMipmapLodBias(float bias)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureLodBias, bias);
        }

        /// <summary>
        /// GL_ARB_seamless_cubemap_per_texture or GL_AMD_seamless_cubemap_per_texture must be available for this to take effect
        /// </summary>
        /// <param name="state"></param>
        public void EnableSeamlessCubemapARB_AMD(bool state)
        {
            if (Helper.IsExtensionsAvailable("GL_AMD_seamless_cubemap_per_texture") || Helper.IsExtensionsAvailable("GL_ARB_seamless_cubemap_per_texture"))
            {
                GL.TextureParameter(ID, TextureParameterName.TextureCubeMapSeamless, state ? 1 : 0);
            }
        }

        public void BindToImageUnit(int unit, InternalFormat format, int layer = 0, bool layered = false, int level = 0)
        {
            GL.BindImageTexture(unit, ID, level, layered, layer, TextureAccess.ReadWrite, (SizedInternalFormat)format);
        }
        public void BindToUnit(int unit)
        {
            GL.BindTextureUnit(unit, ID);
        }

        public static void UnbindFromUnit(int unit)
        {
            GL.BindTextureUnit(unit, 0);
        }

        public unsafe void Upload3D<T>(int width, int height, int depth, PixelFormat pixelFormat, PixelType pixelType, in T pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0) where T : unmanaged
        {
            fixed (void* ptr = &pixels)
            {
                Upload3D(width, height, depth, pixelFormat, pixelType, (nint)ptr, level, xOffset, yOffset, zOffset);
            }
        }
        public void Upload3D(int width, int height, int depth, PixelFormat pixelFormat, PixelType pixelType, nint pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0)
        {
            GL.TextureSubImage3D(ID, level, xOffset, yOffset, zOffset, width, height, depth, pixelFormat, pixelType, pixels);
        }

        public unsafe void Upload2D<T>(int width, int height, PixelFormat pixelFormat, PixelType pixelType, in T pixels, int level = 0, int xOffset = 0, int yOffset = 0) where T : unmanaged
        {
            fixed (void* ptr = &pixels)
            {
                Upload2D(width, height, pixelFormat, pixelType, (nint)ptr, level, xOffset, yOffset);
            }
        }
        public void Upload2D(int width, int height, PixelFormat pixelFormat, PixelType pixelType, nint pixels, int level = 0, int xOffset = 0, int yOffset = 0)
        {
            GL.TextureSubImage2D(ID, level, xOffset, yOffset, width, height, pixelFormat, pixelType, pixels);
        }

        public void UploadCompressed2D(int width, int height, nint pixels, int level = 0, int xOffset = 0, int yOffset = 0)
        {
            int imageSize = GetBlockCompressedImageSize(TextureFormat, width, height, 1);
            GL.CompressedTextureSubImage2D(ID, level, xOffset, yOffset, width, height, (PixelFormat)TextureFormat, imageSize, pixels);
        }

        public void GetImageData(PixelFormat pixelFormat, PixelType pixelType, nint pixels, int bufSize, int level = 0)
        {
            GL.GetTextureImage(ID, level, pixelFormat, pixelType, bufSize, pixels);
        }

        public unsafe void Clear<T>(PixelFormat pixelFormat, PixelType pixelType, in T value, int level = 0) where T : unmanaged
        {
            fixed (void* ptr = &value)
            {
                Clear(pixelFormat, pixelType, (nint)ptr, level);
            }
        }
        public void Clear(PixelFormat pixelFormat, PixelType pixelType, nint value, int level = 0)
        {
            GL.ClearTexImage(ID, level, pixelFormat, pixelType, value);
        }

        public void GenerateMipmap()
        {
            GL.GenerateTextureMipmap(ID);
        }

        public void ImmutableAllocate(int width, int height, int depth, InternalFormat format, int levels = 1)
        {
            switch (TextureType)
            {
                case Type.Texture2D:
                case Type.Cubemap:
                    GL.TextureStorage2D(ID, levels, (SizedInternalFormat)format, width, height);
                    Width = width; Height = height; Depth = 1;
                    Levels = levels;
                    break;

                case Type.Texture3D:
                    GL.TextureStorage3D(ID, levels, (SizedInternalFormat)format, width, height, depth);
                    Width = width; Height = height; Depth = depth;
                    Levels = levels;
                    break;

                default:
                    throw new UnreachableException();
            }
            TextureFormat = format;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public long GetTextureHandleARB()
        {
            long textureHandle = GL.Arb.GetTextureHandle(ID);
            GL.Arb.MakeTextureHandleResident(textureHandle);
            associatedTextureHandles.Add(textureHandle);
            return textureHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public long GetTextureHandleARB(Sampler samplerObject)
        {
            long textureHandle = GL.Arb.GetTextureSamplerHandle(ID, samplerObject.ID);
            GL.Arb.MakeTextureHandleResident(textureHandle);
            associatedTextureHandles.Add(textureHandle);
            return textureHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public long GetImageHandleARB(InternalFormat format, int layer = 0, bool layered = false, int level = 0)
        {
            long imageHandle = GL.Arb.GetImageHandle(ID, level, layered, layer, (PixelFormat)format);

            if (true)
            {
                // Workarround for AMD driver bug when using GL_READ_WRITE or GL_WRITE_ONLY.
                // You'd think having GL_READ_ONLY would create other problems since we write to the them,
                // but it works and is the best we can do until the bug is fixed.
                GL.Arb.MakeImageHandleResident(imageHandle, All.ReadOnly);
            }
            else
            {
                GL.Arb.MakeImageHandleResident(imageHandle, All.ReadWrite);
            }
            associatedImageHandles.Add(imageHandle);
            return imageHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        private static void UnmakeTextureHandleARB(long textureHandle)
        {
            GL.Arb.MakeTextureHandleNonResident(textureHandle);
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        private static void UnmakeImageHandleARB(long imageHandle)
        {
            GL.Arb.MakeImageHandleNonResident(imageHandle);
        }

        public void Dispose()
        {
            for (int i = 0; i < associatedTextureHandles.Count; i++)
            {
                UnmakeTextureHandleARB(associatedTextureHandles[i]);
            }
            associatedTextureHandles.Clear();

            for (int i = 0; i < associatedImageHandles.Count; i++)
            {
                UnmakeImageHandleARB(associatedImageHandles[i]);
            }
            associatedImageHandles.Clear();

            GL.DeleteTexture(ID);
        }

        public static int GetMaxMipmapLevel(int width, int height, int depth)
        {
            return MathF.ILogB(Math.Max(width, Math.Max(height, depth))) + 1;
        }

        public static Vector3i GetMipMapLevelSize(int width, int height, int depth, int level)
        {
            Vector3i size = new Vector3i(width, height, depth) / (1 << level);
            return Vector3i.ComponentMax(size, new Vector3i(1));
        }

        public static int GetBlockCompressedImageSize(InternalFormat format, int width, int height, int depth)
        {
            // Returns the same as KTX2.GetImageSize()
            // Source: https://github.com/JuanDiegoMontoya/Fwog/blob/a26365764fbcca77dfdef0184f1aaff1825c605f/src/Texture.cpp#L22
            //         https://www.reedbeta.com/blog/understanding-bcn-texture-compression-formats/
            //         https://themaister.net/blog/2020/08/12/compressed-gpu-texture-formats-a-review-and-compute-shader-decoders-part-1/

            // BCn formats store 4x4 blocks of pixels, even if the dimensions aren't a multiple of 4
            // We round up to the nearest multiple of 4 for width and height, but not depth, since
            // 3D BCn images are just multiple 2D images stacked
            width = (width + 4 - 1) & -4;
            height = (height + 4 - 1) & -4;

            switch (format)
            {
                // BC1 and BC4 store 4x4 blocks with 64 bits (8 bytes)
                case InternalFormat.BC1RgbUnorm:
                case InternalFormat.BC4RUnorm:
                    return width * height * depth / 2;

                // BC3, BC5, BC6, BC7 and ASTC store 4x4 blocks with 128 bits (16 bytes)
                case InternalFormat.BC5RgUnorm:
                case InternalFormat.BC7RgbaUnorm:
                case InternalFormat.BC7RgbaSrgb:
                case InternalFormat.Astc4X4Rgba:
                case InternalFormat.Astc4X4RgbaSrgb:
                    return width * height * depth;

                default:
                    throw new NotSupportedException($"{nameof(format)} = {format} not known");
            }
        }
    }
}
