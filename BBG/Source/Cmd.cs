using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public static class Cmd
        {
            [Flags]
            public enum MemoryBarrierMask : uint
            {
                TextureFetchBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.TextureFetchBarrierBit,
                CommandBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.CommandBarrierBit,
                ShaderImageAccessBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.ShaderImageAccessBarrierBit,
                ShaderStorageBarrierBit = OpenTK.Graphics.OpenGL.MemoryBarrierMask.ShaderStorageBarrierBit,
            }

            public static void Finish()
            {
                GL.Finish();
            }

            public static void Flush()
            {
                GL.Flush();
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
                GL.BindImageTexture((uint)unit, texture.ID, level, layered, layer, BufferAccess.ReadWrite, (OpenTK.Graphics.OpenGL.InternalFormat)format);
            }

            public static void UnbindTextureUnit(int unit)
            {
                GL.BindTextureUnit((uint)unit, 0);
            }
        }
    }
}
