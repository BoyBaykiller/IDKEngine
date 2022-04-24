using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    class Framebuffer : IDisposable
    {
        public readonly int ID;
        public Framebuffer()
        {
            GL.CreateFramebuffers(1, out ID);
        }

        public void Clear(ClearBufferMask clearBufferMask)
        {
            GL.Clear(clearBufferMask);
        }

        public void ClearBuffer(ClearBuffer clearBuffer, int drawBuffer, float clearValue)
        {
            GL.ClearNamedFramebuffer(ID, clearBuffer, drawBuffer, ref clearValue);
        }

        public void ClearBuffer(ClearBuffer clearBuffer, int drawBuffer, int clearValue)
        {
            GL.ClearNamedFramebuffer(ID, clearBuffer, drawBuffer, ref clearValue);
        }

        public void SetRenderTarget(FramebufferAttachment framebufferAttachment, Texture texture, int level = 0)
        {
            GL.NamedFramebufferTexture(ID, framebufferAttachment, texture.ID, level);
        }

        public void SetTextureLayer(FramebufferAttachment framebufferAttachment, Texture texture, int layer, int level = 0)
        {
            GL.NamedFramebufferTextureLayer(ID, framebufferAttachment, texture.ID, level, layer);
        }

        public void SetRenderBuffer(Renderbuffer renderbuffer, FramebufferAttachment framebufferAttachment)
        {
            GL.NamedFramebufferRenderbuffer(ID, framebufferAttachment, RenderbufferTarget.Renderbuffer, renderbuffer.ID);
        }

        public unsafe void SetDrawBuffers(Span<DrawBuffersEnum> drawBuffersEnums)
        {
            fixed (DrawBuffersEnum* ptr = drawBuffersEnums)
            {
                GL.NamedFramebufferDrawBuffers(ID, drawBuffersEnums.Length, ptr);
            }
        }
        public void SetReadBuffer(ReadBufferMode readBufferMode)
        {
            GL.NamedFramebufferReadBuffer(ID, readBufferMode);
        }

        public void Bind(FramebufferTarget framebufferTarget = FramebufferTarget.Framebuffer)
        {
            GL.BindFramebuffer(framebufferTarget, ID);
        }

        public static void Bind(int id, FramebufferTarget framebufferTarget = FramebufferTarget.Framebuffer)
        {
            GL.BindFramebuffer(framebufferTarget, id);
        }

        public FramebufferStatus GetStatus()
        {
            return GL.CheckNamedFramebufferStatus(ID, FramebufferTarget.Framebuffer);
        }

        public void GetPixels(int x, int y, int width, int height, PixelFormat pixelFormat, PixelType pixelType, IntPtr pixels)
        {
            Bind(FramebufferTarget.ReadFramebuffer);
            GL.ReadPixels(x, y, width, height, pixelFormat, pixelType, pixels);
            GL.Finish();
        }

        public void GetPixels<T>(int x, int y, int width, int height, PixelFormat pixelFormat, PixelType pixelType, T[] pixels) where T : struct
        {
            Bind(FramebufferTarget.ReadFramebuffer);
            GL.ReadPixels(x, y, width, height, pixelFormat, pixelType, pixels);
            GL.Finish();
        }

        public void GetPixels<T>(int x, int y, int width, int height, PixelFormat pixelFormat, PixelType pixelType, ref T pixels) where T : struct
        {
            Bind(FramebufferTarget.ReadFramebuffer);
            GL.ReadPixels(x, y, width, height, pixelFormat, pixelType, ref pixels);
            GL.Finish();
        }

        public void Dispose()
        {
            GL.DeleteFramebuffer(ID);
        }
    }
}
