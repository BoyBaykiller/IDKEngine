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

        public void ClearBuffer(ClearBuffer clearBuffer, int drawBuffer, uint clearValue)
        {
            GL.ClearNamedFramebuffer((uint)ID, clearBuffer, drawBuffer, ref clearValue);
        }

        public void ClearBuffer(ClearBuffer clearBuffer, int drawBuffer, int clearValue)
        {
            GL.ClearNamedFramebuffer(ID, clearBuffer, drawBuffer, ref clearValue);
        }

        public void SetRenderTarget(FramebufferAttachment framebufferAttachment, Texture texture, int level = 0)
        {
            GL.NamedFramebufferTexture(ID, framebufferAttachment, texture.ID, level);
        }

        public void SetRenderTargetLayer(FramebufferAttachment framebufferAttachment, Texture texture, int layer, int level = 0)
        {
            GL.NamedFramebufferTextureLayer(ID, framebufferAttachment, texture.ID, level, layer);
        }

        public unsafe void SetDrawBuffers(ReadOnlySpan<DrawBuffersEnum> drawBuffersEnums)
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

        public void SetParamater(FramebufferDefaultParameter framebufferDefaultParameter, int param)
        {
            GL.NamedFramebufferParameter(ID, framebufferDefaultParameter, param);
        }

        public void Bind(FramebufferTarget framebufferTarget = FramebufferTarget.Framebuffer)
        {
            GL.BindFramebuffer(framebufferTarget, ID);
        }

        public FramebufferStatus GetStatus()
        {
            return GL.CheckNamedFramebufferStatus(ID, FramebufferTarget.Framebuffer);
        }

        public void Dispose()
        {
            GL.DeleteFramebuffer(ID);
        }


        public static void Bind(int id, FramebufferTarget framebufferTarget = FramebufferTarget.Framebuffer)
        {
            GL.BindFramebuffer(framebufferTarget, id);
        }
        public static void Clear(int id, ClearBufferMask clearBufferMask, FramebufferTarget framebufferTarget = FramebufferTarget.Framebuffer)
        {
            GL.BindFramebuffer(framebufferTarget, id);
            GL.Clear(clearBufferMask);
        }
    }
}
