using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using System.Collections.Generic;

namespace IDKEngine.Render.Objects
{
    class Texture : IDisposable
    {
        public enum TextureDimension : int
        {
            Undefined,
            One,
            Two,
            Three,
        }

        public Vector2i Size => new Vector2i(Width, Height);

        public readonly int ID;
        public readonly TextureTarget Target;
        public readonly TextureDimension Dimension;
        public int Width { get; private set; } = 1;
        public int Height { get; private set; } = 1;
        public int Depth { get; private set; } = 1;
        public SizedInternalFormat SizedInternalFormat { get; private set; }

        private readonly List<ulong> associatedTextureHandles = new List<ulong>();

        private static readonly int dummyTexture = GetDummyTexture(TextureTarget.Texture2D);
        private static int GetDummyTexture(TextureTarget textureTarget)
        {
            GL.CreateTextures(textureTarget, 1, out int id);
            return id;
        }

        public Texture(TextureTarget3d textureTarget3D)
        {
            Target = (TextureTarget)textureTarget3D;
            Dimension = TextureDimension.Three;

            GL.CreateTextures(Target, 1, out ID);
        }

        public Texture(TextureTarget2d textureTarget2D)
        {
            Target = (TextureTarget)textureTarget2D;
            Dimension = TextureDimension.Two;

            GL.CreateTextures(Target, 1, out ID);
        }

        public Texture(TextureTarget1d textureTarget1D)
        {
            Target = (TextureTarget)textureTarget1D;
            Dimension = TextureDimension.One;

            GL.CreateTextures(Target, 1, out ID);
        }

        public Texture(TextureBufferTarget textureBufferTarget, BufferObject bufferObject, SizedInternalFormat sizedInternalFormat)
        {
            Target = (TextureTarget)textureBufferTarget;
            Dimension = TextureDimension.Undefined;

            GL.CreateTextures(Target, 1, out ID);
            GL.TextureBuffer(ID, sizedInternalFormat, bufferObject.ID);
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

        public void SetSwizzleR(All swizzle)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureSwizzleR, (int)swizzle);
        }
        public void SetSwizzleG(All swizzle)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureSwizzleG, (int)swizzle);
        }
        public void SetSwizzleB(All swizzle)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureSwizzleB, (int)swizzle);
        }
        public void SetSwizzleA(All swizzle)
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

        public unsafe void SetBorderColor(Vector4 color)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureBorderColor, &color.X);
        }

        public void SetMipmapLodBias(float bias)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureLodBias, bias);
        }

        public void Bind()
        {
            GL.BindTexture(Target, ID);
        }

        public void BindToImageUnit(int unit, int level, bool layered, int layer, TextureAccess textureAccess, SizedInternalFormat sizedInternalFormat)
        {
            GL.BindImageTexture(unit, ID, level, layered, layer, textureAccess, sizedInternalFormat);
        }
        public void BindToUnit(int unit)
        {
            GL.BindTextureUnit(unit, ID);
        }

        public static void UnbindFromUnit(int unit)
        {
            GL.BindTextureUnit(unit, dummyTexture);
        }

        public static void MultiBindToUnit(int first, int[] textures)
        {
            GL.BindTextures(first, textures.Length, textures);
        }

        public static unsafe void MultiBindToUnit(int first, int length, int* textures)
        {
            GL.BindTextures(first, length, textures);
        }

        public void SubTexture3D<T>(int width, int height, int depth, PixelFormat pixelFormat, PixelType pixelType, T[] pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0) where T : unmanaged
        {
            SubTexture3D(width, height, depth, pixelFormat, pixelType, pixels[0], level, xOffset, yOffset, zOffset);
        }
        public unsafe void SubTexture3D<T>(int width, int height, int depth, PixelFormat pixelFormat, PixelType pixelType, in T pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0) where T : unmanaged
        {
            fixed (void* ptr = &pixels)
            {
                SubTexture3D(width, height, depth, pixelFormat, pixelType, (IntPtr)ptr, level, xOffset, yOffset, zOffset);
            }
        }
        public void SubTexture3D(int width, int height, int depth, PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0)
        {
            GL.TextureSubImage3D(ID, level, xOffset, yOffset, zOffset, width, height, depth, pixelFormat, pixelType, pixels);
        }

        public void SubTexture2D<T>(int width, int height, PixelFormat pixelFormat, PixelType pixelType, T[] pixels, int level = 0, int xOffset = 0, int yOffset = 0) where T : unmanaged
        {
            SubTexture2D(width, height, pixelFormat, pixelType, pixels[0], level, xOffset, yOffset);
        }
        public unsafe void SubTexture2D<T>(int width, int height, PixelFormat pixelFormat, PixelType pixelType, in T pixels, int level = 0, int xOffset = 0, int yOffset = 0) where T : unmanaged
        {
            fixed (void* ptr = &pixels)
            {
                SubTexture2D(width, height, pixelFormat, pixelType, (IntPtr)ptr, level, xOffset, yOffset);
            }
        }
        public void SubTexture2D(int width, int height, PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels, int level = 0, int xOffset = 0, int yOffset = 0)
        {
            GL.TextureSubImage2D(ID, level, xOffset, yOffset, width, height, pixelFormat, pixelType, pixels);
        }

        public void SubTexture1D<T>(int width, PixelFormat pixelFormat, PixelType pixelType, T[] pixels, int level = 0, int xOffset = 0) where T : unmanaged
        {
            SubTexture1D(width, pixelFormat, pixelType, pixels[0], level, xOffset);
        }
        public unsafe void SubTexture1D<T>(int width, PixelFormat pixelFormat, PixelType pixelType, in T pixels, int level = 0, int xOffset = 0) where T : unmanaged
        {
            fixed (void* ptr = &pixels)
            {
                SubTexture1D(width, pixelFormat, pixelType, (IntPtr)ptr, level, xOffset);
            }
        }
        public void SubTexture1D(int width, PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels, int level = 0, int xOffset = 0)
        {
            GL.TextureSubImage1D(ID, level, xOffset, width, pixelFormat, pixelType, pixels);
        }

        public void GetImageData(PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels, int bufSize, int level = 0)
        {
            GL.GetTextureImage(ID, level, pixelFormat, pixelType, bufSize, pixels);
        }

        public unsafe void Clear<T>(PixelFormat pixelFormat, PixelType pixelType, in T value, int level = 0) where T : unmanaged
        {
            fixed (void* ptr = &value)
            {
                Clear(pixelFormat, pixelType, (IntPtr)ptr, level);
            }
        }
        public void Clear(PixelFormat pixelFormat, PixelType pixelType, IntPtr value, int level = 0)
        {
            GL.ClearTexImage(ID, level, pixelFormat, pixelType, value);
        }

        public void GenerateMipmap()
        {
            GL.GenerateTextureMipmap(ID);
        }

        public static int GetMaxMipmapLevel(int width, int height, int depth)
        {
            return MathF.ILogB(Math.Max(width, Math.Max(height, depth))) + 1;
        }

        public static Vector3i GetMipMapLevelSize(int width, int height, int depth, int level)
        {
            return new Vector3i(width / (1 << level), height / (1 << level), depth / (1 << level));
        }

        public void ImmutableAllocate(int width, int height, int depth, SizedInternalFormat sizedInternalFormat, int levels = 1)
        {
            switch (Dimension)
            {
                case TextureDimension.One:
                    GL.TextureStorage1D(ID, levels, sizedInternalFormat, width);
                    Width = width;
                    break;

                case TextureDimension.Two:
                    GL.TextureStorage2D(ID, levels, sizedInternalFormat, width, height);
                    Width = width; Height = height;
                    break;

                case TextureDimension.Three:
                    GL.TextureStorage3D(ID, levels, sizedInternalFormat, width, height, depth);
                    Width = width; Height = height; Depth = depth;
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
        public ulong GetTextureHandleARB()
        {
            ulong textureHandle = (ulong)GL.Arb.GetTextureHandle(ID);
            GL.Arb.MakeTextureHandleResident(textureHandle);
            associatedTextureHandles.Add(textureHandle);
            return textureHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public ulong GetTextureHandleARB(SamplerObject samplerObject)
        {
            ulong textureHandle = (ulong)GL.Arb.GetTextureSamplerHandle(ID, samplerObject.ID);
            GL.Arb.MakeTextureHandleResident(textureHandle);
            associatedTextureHandles.Add(textureHandle);
            return textureHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        private static void UnmakeTextureHandleARB(ulong textureHandle)
        {
            GL.Arb.MakeTextureHandleNonResident(textureHandle);
        }

        public void GetSizeMipmap(out int width, out int height, out int depth, int level = 0)
        {
            GL.GetTextureLevelParameter(ID, level, GetTextureParameter.TextureWidth, out width);
            GL.GetTextureLevelParameter(ID, level, GetTextureParameter.TextureHeight, out height);
            GL.GetTextureLevelParameter(ID, level, GetTextureParameter.TextureDepth, out depth);
        }

        public void Dispose()
        {
            for (int i = 0; i < associatedTextureHandles.Count; i++)
            {
                UnmakeTextureHandleARB(associatedTextureHandles[i]);
            }
            GL.DeleteTexture(ID);
        }
    }
}
