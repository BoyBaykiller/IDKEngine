using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Utils;

namespace IDKEngine.OpenGL
{
    class Texture : IDisposable
    {
        public enum TextureDimensions : int
        {
            Two,
            Three,
        }

        public readonly int ID;
        public readonly TextureTarget Target;
        public readonly TextureDimensions Dimension;
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }
        public int Levels { get; private set; }

        public SizedInternalFormat SizedInternalFormat { get; private set; }

        private readonly List<long> associatedTextureHandles = new List<long>(0);
        private readonly List<long> associatedImageHandles = new List<long>(0);

        private static readonly int dummyTexture = GetDummyTexture(TextureTarget.Texture2D);
        private static int GetDummyTexture(TextureTarget textureTarget)
        {
            GL.CreateTextures(textureTarget, 1, out int id);
            return id;
        }

        public Texture(TextureTarget3d textureTarget3D)
        {
            Target = (TextureTarget)textureTarget3D;
            Dimension = TextureDimensions.Three;

            GL.CreateTextures(Target, 1, out ID);
        }

        public Texture(TextureTarget2d textureTarget2D)
        {
            Target = (TextureTarget)textureTarget2D;
            Dimension = TextureDimensions.Two;

            GL.CreateTextures(Target, 1, out ID);
        }

        public void SetFilter(TextureMinFilter minFilter, TextureMagFilter magFilter)
        {
            /// Explanation for Mipmap filters from https://learnopengl.com/Getting-started/Textures:
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

        public void SetCompareMode(TextureCompareMode textureCompareMode)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureCompareMode, (int)textureCompareMode);
        }

        public void SetCompareFunc(All textureCompareFunc)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureCompareFunc, (int)textureCompareFunc);
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

        public void Bind()
        {
            GL.BindTexture(Target, ID);
        }

        public void BindToImageUnit(int unit, SizedInternalFormat sizedInternalFormat, int layer = 0, bool layered = false, int level = 0)
        {
            GL.BindImageTexture(unit, ID, level, layered, layer, TextureAccess.ReadWrite, sizedInternalFormat);
        }
        public void BindToUnit(int unit)
        {
            GL.BindTextureUnit(unit, ID);
        }

        public static void UnbindFromUnit(int unit)
        {
            GL.BindTextureUnit(unit, dummyTexture);
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
            int imageSize = GetBlockCompressedImageSize(SizedInternalFormat, width, height, 1);
            GL.CompressedTextureSubImage2D(ID, level, xOffset, yOffset, width, height, (PixelFormat)SizedInternalFormat, imageSize, pixels);
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

        public void ImmutableAllocate(int width, int height, int depth, SizedInternalFormat sizedInternalFormat, int levels = 1)
        {
            switch (Dimension)
            {
                case TextureDimensions.Two:
                    GL.TextureStorage2D(ID, levels, sizedInternalFormat, width, height);
                    Width = width; Height = height; Depth = 1;
                    Levels = levels;
                    break;

                case TextureDimensions.Three:
                    GL.TextureStorage3D(ID, levels, sizedInternalFormat, width, height, depth);
                    Width = width; Height = height; Depth = depth;
                    Levels = levels;
                    break;

                default:
                    return;
            }
            SizedInternalFormat = sizedInternalFormat;
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
        public long GetImageHandleARB(SizedInternalFormat sizedInternalFormat, int layer = 0, bool layered = false, int level = 0)
        {
            long imageHandle = GL.Arb.GetImageHandle(ID, level, layered, layer, (PixelFormat)sizedInternalFormat);

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

        public static int GetBlockCompressedImageSize(SizedInternalFormat internalFormat, int width, int height, int depth)
        {
            // Returns same as KTX2.GetImageSize()

            // Source: https://github.com/JuanDiegoMontoya/Fwog/blob/a26365764fbcca77dfdef0184f1aaff1825c605f/src/Texture.cpp#L22

            // BCn formats store 4x4 blocks of pixels, even if the dimensions aren't a multiple of 4
            // We round up to the nearest multiple of 4 for width and height, but not depth, since
            // 3D BCn images are just multiple 2D images stacked
            width = (width + 4 - 1) & -4;
            height = (height + 4 - 1) & -4;

            switch (internalFormat)
            {
                // BC1 and BC4 store 4x4 blocks with 64 bits (8 bytes)
                case SizedInternalFormat.CompressedRgbS3tcDxt1Ext:
                case SizedInternalFormat.CompressedRgbaS3tcDxt1Ext:
                case SizedInternalFormat.CompressedSrgbS3tcDxt1Ext:
                case SizedInternalFormat.CompressedSrgbAlphaS3tcDxt1Ext:
                case SizedInternalFormat.CompressedRedRgtc1:
                case SizedInternalFormat.CompressedSignedRedRgtc1:
                    return width * height * depth / 2;

                // BC3, BC5, BC6, and BC7 store 4x4 blocks with 128 bits (16 bytes)
                case SizedInternalFormat.CompressedRgbaS3tcDxt3Ext:
                case SizedInternalFormat.CompressedSrgbAlphaS3tcDxt3Ext:
                case SizedInternalFormat.CompressedRgbaS3tcDxt5Ext:
                case SizedInternalFormat.CompressedSrgbAlphaS3tcDxt5Ext:
                case SizedInternalFormat.CompressedRgRgtc2:
                case SizedInternalFormat.CompressedSignedRgRgtc2:
                case SizedInternalFormat.CompressedRgbBptcUnsignedFloat:
                case SizedInternalFormat.CompressedRgbBptcSignedFloat:
                case SizedInternalFormat.CompressedRgbaBptcUnorm:
                case SizedInternalFormat.CompressedSrgbAlphaBptcUnorm:
                    return width * height * depth;

                default:
                    throw new NotSupportedException($"{nameof(internalFormat)} = {internalFormat} not known");
            }
        }
    }
}
