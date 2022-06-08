using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using PixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;

namespace IDKEngine.Render.Objects
{
    class Texture : IDisposable
    {
        public enum PixelTypeSize : int
        {
            TextureRedSize = 32860,
            TextureGreenSize = 32861,
            TextureBlueSize = 32862,
            TextureAlphaSize = 32863,
        }

        public enum TextureDimension : byte
        {
            Undefined = 0,
            One = 1,
            Two = 2,
            Three = 3,
        }

        public readonly int ID;
        public readonly TextureTarget Target;
        public readonly TextureDimension Dimension;
        public int Width { get; private set; } = 1;
        public int Height { get; private set; } = 1;
        public int Depth { get; private set; } = 1;
        public PixelInternalFormat PixelInternalFormat { get; private set; }

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

        public Texture(TextureBufferTarget textureBufferTarget, BufferObject bufferObject, SizedInternalFormat sizedInternalFormat = SizedInternalFormat.Rgba32f)
        {
            Target = (TextureTarget)textureBufferTarget;
            Dimension = TextureDimension.Undefined;

            GL.CreateTextures(Target, 1, out ID);
            GL.TextureBuffer(ID, sizedInternalFormat, bufferObject.ID);
            GL.TextureBufferRange(ID, sizedInternalFormat, bufferObject.ID, IntPtr.Zero, bufferObject.Size);
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="value">Range of [1.0; MaxTextureMaxAnisotropy]</param>
        public void SetAnisotropy(float value)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureMaxAnisotropy, value);
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

        public void SetCompareMode(TextureCompareMode textureCompareMode)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureCompareMode, (int)textureCompareMode);
        }

        public void SetCompareFunc(All textureCompareFunc)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureCompareFunc, (int)textureCompareFunc);
        }

        /// <summary>
        /// GL_ARB_seamless_cubemap_per_texture or GL_AMD_seamless_cubemap_per_texture must be available
        /// </summary>
        /// <param name="state"></param>
        public void SetSeamlessCubeMapPerTextureARB_AMD(bool state)
        {
            GL.TextureParameter(ID, TextureParameterName.TextureCubeMapSeamless, state ? 1 : 0);
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

        public void SubTexture3D<T>(int width, int heigth, int depth, PixelFormat pixelFormat, PixelType pixelType, T[,,] pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0) where T : struct
        {
            GL.TextureSubImage3D(ID, level, xOffset, yOffset, zOffset, width, heigth, depth, pixelFormat, pixelType, pixels);
        }
        public void SubTexture3D<T>(int width, int heigth, int depth, PixelFormat pixelFormat, PixelType pixelType, T[] pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0) where T : struct
        {
            GL.TextureSubImage3D(ID, level, xOffset, yOffset, zOffset, width, heigth, depth, pixelFormat, pixelType, pixels);
        }
        public void SubTexture3D(int width, int heigth, int depth, PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0)
        {
            GL.TextureSubImage3D(ID, level, xOffset, yOffset, zOffset, width, heigth, depth, pixelFormat, pixelType, pixels);
        }

        public void SubTexture2D<T>(int width, int heigth, PixelFormat pixelFormat, PixelType pixelType, T[,] pixels, int level = 0, int xOffset = 0, int yOffset = 0) where T : struct
        {
            GL.TextureSubImage2D(ID, level, xOffset, yOffset, width, heigth, pixelFormat, pixelType, pixels);
        }
        public void SubTexture2D<T>(int width, int heigth, PixelFormat pixelFormat, PixelType pixelType, T[] pixels, int level = 0, int xOffset = 0, int yOffset = 0) where T : struct
        {
            GL.TextureSubImage2D(ID, level, xOffset, yOffset, width, heigth, pixelFormat, pixelType, pixels);
        }
        public void SubTexture2D(int width, int heigth, PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels, int level = 0, int xOffset = 0, int yOffset = 0)
        {
            GL.TextureSubImage2D(ID, level, xOffset, yOffset, width, heigth, pixelFormat, pixelType, pixels);
        }

        public void SubTexture1D<T>(int width, PixelFormat pixelFormat, PixelType pixelType, T[] pixels, int level = 0, int xOffset = 0) where T : struct
        {
            GL.TextureSubImage1D(ID, level, xOffset, width, pixelFormat, pixelType, pixels);
        }
        public void SubTexture1D(int width, PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels, int level = 0, int xOffset = 0)
        {
            GL.TextureSubImage1D(ID, level, xOffset, width, pixelFormat, pixelType, pixels);
        }

        public void Clear<T>(PixelFormat pixelFormat, PixelType pixelType, T value, int level = 0) where T : struct
        {
            GL.ClearTexImage(ID, level, pixelFormat, pixelType, ref value);
        }


        /// <summary>
        /// To properly generate mipmaps <see cref="TextureMinFilter"/> must be set to one of the mipmap options 
        /// and if immutable storage is used the level parameter should match the number of desired mipmap levels to generate (default: 1).
        /// </summary>
        public void GenerateMipmap()
        {
            GL.GenerateTextureMipmap(ID);
        }

        public static int GetMaxMipmapLevel(int width, int height, int depth)
        {
            return (int)MathF.Ceiling(MathF.Log2(Math.Max(width, Math.Max(height, depth))));
        }

        public static Vector3i GetMipMapLevelSize(int width, int height, int depth, int level)
        {
            return new Vector3i(width / (1 << level), height / (1 << level), depth / (1 << level));
        }

        public void MutableAllocate(int width, int height, int depth, PixelInternalFormat pixelInternalFormat, IntPtr intPtr, PixelFormat pixelFormat, PixelType pixelType)
        {
            Bind();
            switch (Dimension)
            {
                case TextureDimension.One:
                    GL.TexImage1D(Target, 0, pixelInternalFormat, width, 0, pixelFormat, pixelType, intPtr);
                    Width = width;
                    break;

                case TextureDimension.Two:
                    if (Target == TextureTarget.TextureCubeMap)
                        for (int i = 0; i < 6; i++)
                            GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, pixelInternalFormat, width, height, 0, pixelFormat, pixelType, intPtr);
                    else
                        GL.TexImage2D(Target, 0, pixelInternalFormat, width, height, 0, pixelFormat, pixelType, intPtr);
                    Width = width; Height = height;
                    break;

                case TextureDimension.Three:
                    GL.TexImage3D(Target, 0, pixelInternalFormat, width, height, depth, 0, pixelFormat, pixelType, intPtr);
                    Width = width; Height = height; Depth = depth;
                    break;

                default:
                    return;
            }
            PixelInternalFormat = pixelInternalFormat;
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
            PixelInternalFormat = (PixelInternalFormat)sizedInternalFormat;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public long MakeHandleResidentARB()
        {
            long textureHandle = GL.Arb.GetTextureHandle(ID);
            GL.Arb.MakeTextureHandleResident(textureHandle);
            return textureHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public long MakeSamplerHandleResidentARB(SamplerObject samplerObject)
        {
            long textureHandle = GL.Arb.GetTextureSamplerHandle(ID, samplerObject.ID);
            GL.Arb.MakeTextureHandleResident(textureHandle);
            return textureHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public static bool UnmakeHandleResidentARB(long textureHandle)
        {
            if (GL.Arb.IsTextureHandleResident(textureHandle))
            {
                GL.Arb.MakeTextureHandleNonResident(textureHandle);
                return true;
            }
            return false;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public long MakeImageHandleResidentARB(int level, bool layered, int layer, SizedInternalFormat sizedInternalFormat, TextureAccess textureAccess)
        {
            long imageHandle = GL.Arb.GetImageHandle(ID, level, layered, layer, (PixelFormat)sizedInternalFormat);
            GL.Arb.MakeImageHandleResident(imageHandle, (All)textureAccess);
            return imageHandle;
        }

        /// <summary>
        /// GL_ARB_bindless_texture must be available
        /// </summary>
        /// <returns></returns>
        public static bool UnmakeImageHandleResidentARB(long imageHandle)
        {
            if (GL.Arb.IsImageHandleResident(imageHandle))
            {
                GL.Arb.MakeImageHandleNonResident(imageHandle);
                return true;
            }
            return false;
        }

        public void GetSizeMipmap(out int width, out int height, out int depth, int level = 0)
        {
            GL.GetTextureLevelParameter(ID, level, GetTextureParameter.TextureWidth, out width);
            GL.GetTextureLevelParameter(ID, level, GetTextureParameter.TextureHeight, out height);
            GL.GetTextureLevelParameter(ID, level, GetTextureParameter.TextureDepth, out depth);
        }

        public int GetPixelTypeComponentSize(PixelTypeSize pixelTypeSize, int level = 0)
        {
            GL.GetTextureLevelParameter(ID, level, (GetTextureParameter)pixelTypeSize, out int bitSize);
            return bitSize / 8;
        }

        public int GetPixelSize(int level = 0)
        {
            int r = GetPixelTypeComponentSize(PixelTypeSize.TextureRedSize, level);
            int g = GetPixelTypeComponentSize(PixelTypeSize.TextureGreenSize, level);
            int b = GetPixelTypeComponentSize(PixelTypeSize.TextureBlueSize, level);
            int a = GetPixelTypeComponentSize(PixelTypeSize.TextureAlphaSize, level);

            return r + g + b + a;
        }

        public void Dispose()
        {
            GL.DeleteTexture(ID);
        }
    }
}
