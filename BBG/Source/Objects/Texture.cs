using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public unsafe class Texture : IDisposable
        {
            public enum Type : uint
            {
                Cubemap = TextureTarget.TextureCubeMap,
                Texture2D = TextureTarget.Texture2d,
                Texture3D = TextureTarget.Texture3d,
            }
            
            public enum InternalFormat : uint
            {
                // UNorm = [ 0, 1] range
                // SNorm = [-1, 1] range
                // Float = float range
                // Uint  = uint range
                // D     = depth formats
                // S     = stencil formats

                R8Unorm = SizedInternalFormat.R8,
                R8Uint = SizedInternalFormat.R8ui,

                R16Float = SizedInternalFormat.R16f,

                R32Float = SizedInternalFormat.R32f,
                R32Uint = SizedInternalFormat.R32ui,

                R8G8Unorm = SizedInternalFormat.Rg8,
                R8G8Snorm = SizedInternalFormat.Rg8Snorm,

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

                /// <summary>
                /// Requires GL_KHR_texture_compression_astc
                /// </summary>
                Astc4X4RgbaKHR = SizedInternalFormat.CompressedRgbaAstc4x4Khr,

                /// <summary>
                /// Requires GL_KHR_texture_compression_astc
                /// </summary>
                Astc4X4RgbaSrgbKHR = SizedInternalFormat.CompressedSrgb8Alpha8Astc4x4Khr,

                D16Unorm = SizedInternalFormat.DepthComponent16,
                D32Float = SizedInternalFormat.DepthComponent32f,

                S8Uint = SizedInternalFormat.StencilIndex8,

                D24UnormS8Uint = SizedInternalFormat.Depth24Stencil8,
            }

            [Flags]
            public enum InternalFormatType : int
            {
                Color = 1 << 0,
                Depth = 1 << 1,
                Stencil = 1 << 2,
                DepthStencil = Depth | Stencil
            }

            public enum PixelFormat : uint
            {
                R = OpenTK.Graphics.OpenGL.PixelFormat.Red,
                RG = OpenTK.Graphics.OpenGL.PixelFormat.Rg,
                RGB = OpenTK.Graphics.OpenGL.PixelFormat.Rgb,
                RGBA = OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
            }

            public enum PixelType : uint
            {
                UByte = OpenTK.Graphics.OpenGL.PixelType.UnsignedByte,
                Float = OpenTK.Graphics.OpenGL.PixelType.Float,
            }

            public enum Swizzle : uint
            {
                Zero = TextureSwizzle.Zero,
                One = TextureSwizzle.One,
                R = TextureSwizzle.Red,
                G = TextureSwizzle.Green,
                B = TextureSwizzle.Blue,
                A = TextureSwizzle.Alpha,
            }

            public struct BindlessHandle
            {
                public ulong GLHandle;
            }

            public readonly int ID;
            public readonly Type TextureType;
            public InternalFormat Format { get; private set; }
            public int Width { get; private set; }
            public int Height { get; private set; }
            public int Depth { get; private set; }
            public int Levels { get; private set; }

            private List<BindlessHandle> bindlessTextureHandles = new List<BindlessHandle>(0);
            private List<BindlessHandle> bindlessImageHandles = new List<BindlessHandle>(0);

            public Texture(Type textureType)
            {
                GL.CreateTextures((TextureTarget)textureType, 1, ref ID);
                TextureType = textureType;
            }

            public void SetFilter(Sampler.MinFilter minFilter, Sampler.MagFilter magFilter)
            {
                /// GL_NEAREST_MIPMAP_NEAREST: takes the nearest mipmap to match the pixel size and uses nearest neighbor interpolation for texture sampling.
                /// GL_LINEAR_MIPMAP_NEAREST: takes the nearest mipmap level and samples that level using linear interpolation.
                /// GL_NEAREST_MIPMAP_LINEAR: linearly interpolates between the two mipmaps that most closely match the size of a pixel and samples the interpolated level via nearest neighbor interpolation.
                /// GL_LINEAR_MIPMAP_LINEAR: linearly interpolates between the two closest mipmaps and samples the interpolated level via linear interpolation.

                GL.TextureParameteri(ID, TextureParameterName.TextureMinFilter, (int)minFilter);
                GL.TextureParameteri(ID, TextureParameterName.TextureMagFilter, (int)magFilter);
            }

            public void SetWrapMode(Sampler.WrapMode wrapS, Sampler.WrapMode wrapT)
            {
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapS, (int)wrapS);
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapT, (int)wrapT);
            }

            public void SetWrapMode(Sampler.WrapMode wrapS, Sampler.WrapMode wrapT, Sampler.WrapMode wrapR)
            {
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapS, (int)wrapS);
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapT, (int)wrapT);
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapR, (int)wrapR);
            }

            public void SetSwizzleR(Swizzle swizzle)
            {
                GL.TextureParameteri(ID, TextureParameterName.TextureSwizzleR, (int)swizzle);
            }
            public void SetSwizzleG(Swizzle swizzle)
            {
                GL.TextureParameteri(ID, TextureParameterName.TextureSwizzleG, (int)swizzle);
            }
            public void SetSwizzleB(Swizzle swizzle)
            {
                GL.TextureParameteri(ID, TextureParameterName.TextureSwizzleB, (int)swizzle);
            }
            public void SetSwizzleA(Swizzle swizzle)
            {
                GL.TextureParameteri(ID, TextureParameterName.TextureSwizzleA, (int)swizzle);
            }

            public void SetAnisotropy(Sampler.Anisotropy anisotropy)
            {
                GL.TextureParameterf(ID, TextureParameterName.TextureMaxAnisotropy, (float)anisotropy);
            }

            /// <summary>
            /// GL_ARB_seamless_cubemap_per_texture or GL_AMD_seamless_cubemap_per_texture must be available for this to take effect
            /// </summary>
            /// <param name="state"></param>
            public bool TryEnableSeamlessCubemap(bool state)
            {
                if (contextInfo.DeviceInfo.ExtensionSupport.SeamlessCubemapPerTexture)
                {
                    GL.TextureParameteri(ID, (TextureParameterName)All.TextureCubeMapSeamless, state ? 1 : 0);
                    return true;
                }

                return false;
            }

            public void Upload3D<T>(int width, int height, int depth, PixelFormat pixelFormat, PixelType pixelType, in T pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0) where T : unmanaged
            {
                fixed (void* ptr = &pixels)
                {
                    Upload3D(width, height, depth, pixelFormat, pixelType, ptr, level, xOffset, yOffset, zOffset);
                }
            }
            public void Upload3D(int width, int height, int depth, PixelFormat pixelFormat, PixelType pixelType, void* pixels, int level = 0, int xOffset = 0, int yOffset = 0, int zOffset = 0)
            {
                GL.TextureSubImage3D(ID, level, xOffset, yOffset, zOffset, width, height, depth, (OpenTK.Graphics.OpenGL.PixelFormat)pixelFormat, (OpenTK.Graphics.OpenGL.PixelType)pixelType, (nint)pixels);
            }

            public void Upload2D<T>(int width, int height, PixelFormat pixelFormat, PixelType pixelType, in T pixels, int level = 0, int xOffset = 0, int yOffset = 0) where T : unmanaged
            {
                fixed (void* ptr = &pixels)
                {
                    Upload2D(width, height, pixelFormat, pixelType, ptr, level, xOffset, yOffset);
                }
            }
            public void Upload2D(int width, int height, PixelFormat pixelFormat, PixelType pixelType, void* pixels, int level = 0, int xOffset = 0, int yOffset = 0)
            {
                GL.TextureSubImage2D(ID, level, xOffset, yOffset, width, height, (OpenTK.Graphics.OpenGL.PixelFormat)pixelFormat, (OpenTK.Graphics.OpenGL.PixelType)pixelType, (nint)pixels);
            }
            public void Upload2D(Buffer bufferObject, int width, int height, PixelFormat pixelFormat, PixelType pixelType, void* pixels, int level = 0, int xOffset = 0, int yOffset = 0)
            {
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, bufferObject.ID);
                Upload2D(width, height, pixelFormat, pixelType, pixels, level, xOffset, yOffset);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            }

            public void UploadCompressed2D(int width, int height, void* pixels, int level = 0, int xOffset = 0, int yOffset = 0)
            {
                int imageSize = GetBlockCompressedImageSize(Format, width, height, 1);
                GL.CompressedTextureSubImage2D(ID, level, xOffset, yOffset, width, height, (OpenTK.Graphics.OpenGL.InternalFormat)Format, imageSize, (nint)pixels);
            }
            public void UploadCompressed2D(Buffer bufferObject, int width, int height, nint offset, int level = 0, int xOffset = 0, int yOffset = 0)
            {
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, bufferObject.ID);
                UploadCompressed2D(width, height, (void*)offset, level, xOffset, yOffset);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            }

            public void Download(PixelFormat pixelFormat, PixelType pixelType, void* pixels, int bufSize, int level = 0)
            {
                GL.GetTextureImage(ID, level, (OpenTK.Graphics.OpenGL.PixelFormat)pixelFormat, (OpenTK.Graphics.OpenGL.PixelType)pixelType, bufSize, (nint)pixels);
            }

            public void Clear<T>(PixelFormat pixelFormat, PixelType pixelType, in T value, int level = 0) where T : unmanaged
            {
                fixed (void* ptr = &value)
                {
                    Clear(pixelFormat, pixelType, ptr, level);
                }
            }
            public void Clear(PixelFormat pixelFormat, PixelType pixelType, void* data, int level = 0)
            {
                GL.ClearTexImage(ID, level, (OpenTK.Graphics.OpenGL.PixelFormat)pixelFormat, (OpenTK.Graphics.OpenGL.PixelType)pixelType, (nint)data);
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
                        throw new UnreachableException($"Unknown {nameof(TextureType)} = {TextureType}");
                }
                Format = format;
            }

            /// <summary>
            /// Requires GL_ARB_bindless_texture
            /// </summary>
            /// <returns></returns>
            public BindlessHandle GetTextureHandleARB()
            {
                BindlessHandle bindlessHandle = new BindlessHandle();
                bindlessHandle.GLHandle = GL.ARB.GetTextureHandleARB(ID);

                GL.ARB.MakeTextureHandleResidentARB(bindlessHandle.GLHandle);

                bindlessTextureHandles.Add(bindlessHandle);
                return bindlessHandle;
            }

            /// <summary>
            /// Requires GL_ARB_bindless_texture
            /// </summary>
            /// <returns></returns>
            public BindlessHandle GetTextureHandleARB(Sampler samplerObject)
            {
                BindlessHandle bindlessHandle = new BindlessHandle();
                bindlessHandle.GLHandle = GL.ARB.GetTextureSamplerHandleARB(ID, samplerObject.ID);
                
                GL.ARB.MakeTextureHandleResidentARB(bindlessHandle.GLHandle);

                bindlessTextureHandles.Add(bindlessHandle);
                return bindlessHandle;
            }

            /// <summary>
            /// Requires GL_ARB_bindless_texture
            /// </summary>
            /// <returns></returns>
            public BindlessHandle GetImageHandleARB(InternalFormat format, int layer = 0, bool layered = false, int level = 0)
            {
                BindlessHandle bindlessHandle = new BindlessHandle();
                bindlessHandle.GLHandle = GL.ARB.GetImageHandleARB(ID, level, layered, layer, (OpenTK.Graphics.OpenGL.PixelFormat)format);

                GL.ARB.MakeImageHandleResidentARB(bindlessHandle.GLHandle, All.ReadWrite);

                bindlessImageHandles.Add(bindlessHandle);
                return bindlessHandle;
            }

            public void Dispose()
            {
                FramebufferCache.DeleteFramebuffersWithTexture(this);

                for (int i = 0; i < bindlessTextureHandles.Count; i++)
                {
                    GL.ARB.MakeTextureHandleNonResidentARB(bindlessTextureHandles[i].GLHandle);
                }
                bindlessTextureHandles = null;

                for (int i = 0; i < bindlessImageHandles.Count; i++)
                {
                    GL.ARB.MakeImageHandleNonResidentARB(bindlessImageHandles[i].GLHandle);
                }
                bindlessImageHandles = null;

                GL.DeleteTexture(ID);
            }

            public static int GetMaxMipmapLevel(int width, int height, int depth)
            {
                return MathF.ILogB(Math.Max(width, Math.Max(height, depth))) + 1;
            }

            public static Vector3i GetMipmapLevelSize(int width, int height, int depth, int level)
            {
                Vector3i size = new Vector3i(width, height, depth) / (1 << level);
                return Vector3i.ComponentMax(size, new Vector3i(1));
            }

            public static InternalFormatType GetFormatType(InternalFormat internalFormat)
            {
                if (internalFormat == InternalFormat.D16Unorm ||
                    internalFormat == InternalFormat.D32Float)
                {
                    return InternalFormatType.Depth;
                }

                if (internalFormat == InternalFormat.S8Uint)
                {
                    return InternalFormatType.Stencil;
                }

                if (internalFormat == InternalFormat.D24UnormS8Uint)
                {
                    return InternalFormatType.DepthStencil;
                }

                return InternalFormatType.Color;
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
                    case InternalFormat.Astc4X4RgbaKHR:
                    case InternalFormat.Astc4X4RgbaSrgbKHR:
                        return width * height * depth;

                    default:
                        throw new NotSupportedException($"Unknown {nameof(format)} = {format}");
                }
            }

            public static PixelFormat NumChannelsToPixelFormat(int numChannels)
            {
                PixelFormat pixelFormat = numChannels switch
                {
                    1 => PixelFormat.R,
                    2 => PixelFormat.RG,
                    3 => PixelFormat.RGB,
                    4 => PixelFormat.RGBA,
                    _ => throw new NotSupportedException($"Can not convert {nameof(numChannels)} = {numChannels} to {nameof(pixelFormat)}"),
                };
                return pixelFormat;
            }
        }
    }
}
