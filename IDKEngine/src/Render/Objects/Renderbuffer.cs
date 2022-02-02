using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    class Renderbuffer : IDisposable
    {
        public readonly int ID;
        public RenderbufferStorage RenderbufferStorage { get; private set; }
        public Renderbuffer()
        {
            GL.CreateRenderbuffers(1, out ID);
        }

        public void Allocate(RenderbufferStorage renderbufferStorage, int width, int height)
        {
            GL.NamedRenderbufferStorage(ID, renderbufferStorage, width, height);
            RenderbufferStorage = renderbufferStorage;
        }

        public void Dispose()
        {
            GL.DeleteRenderbuffer(ID);
        }
    }
}
