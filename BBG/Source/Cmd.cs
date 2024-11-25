using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public static unsafe class Cmd
        {
            // Application specific
            public const int SET_UNIFORMS_UBO_BLOCK_BINDING = 7;

            [Flags]
            public enum MemoryBarrierMask : uint
            {
                TextureFetchBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.TextureFetchBarrierBit,
                CommandBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.CommandBarrierBit,
                ShaderImageAccessBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.ShaderImageAccessBarrierBit,
                ShaderStorageBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.ShaderStorageBarrierBit,
                All = OpenTK.Graphics.OpenGL.MemoryBarrierMask.AllBarrierBits,
            }

            public static void Finish()
            {
                GL.Finish();
            }

            public static void Flush()
            {
                GL.Flush();
            }

            public static void TextureBarrier()
            {
                GL.TextureBarrier();
            }

            public static void MemoryBarrier(MemoryBarrierMask memoryBarrierMask)
            {
                GL.MemoryBarrier((OpenTK.Graphics.OpenGL.MemoryBarrierMask)memoryBarrierMask);
            }

            public static void UseShaderProgram(ShaderProgram shaderProgram)
            {
                GL.UseProgram(shaderProgram.ID);
            }

            public static void BindTextureUnit(Texture texture, int unit, bool doIt = true)
            {
                if (doIt)
                {
                    GL.BindTextureUnit((uint)unit, texture.ID);
                }
                else
                {
                    UnbindTextureUnit(unit);
                }
            }

            public static void BindImageUnit(Texture texture, int unit, int level = 0, bool layered = false, int layer = 0)
            {
                GL.BindImageTexture((uint)unit, texture.ID, level, layered, layer, BufferAccess.ReadWrite, (InternalFormat)texture.Format);
            }

            public static void BindImageUnit(Texture texture, Texture.InternalFormat format, int unit, int level = 0, bool layered = false, int layer = 0)
            {
                GL.BindImageTexture((uint)unit, texture.ID, level, layered, layer, BufferAccess.ReadWrite, (InternalFormat)format);
            }

            public static void UnbindTextureUnit(int unit)
            {
                GL.BindTextureUnit((uint)unit, 0);
            }

            public static void SetUniforms<T>(in T uniforms) where T : unmanaged
            {
                globalUniformBuffer.InvalidateData();

                globalUniformBuffer.UploadData(0, sizeof(T), uniforms);
                globalUniformBuffer.BindToBufferBackedBlock(Buffer.BufferBackedBlockTarget.Uniform, SET_UNIFORMS_UBO_BLOCK_BINDING);
            }
        }
    }
}
