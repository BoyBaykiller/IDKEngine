using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    class VAO : IDisposable
    {
        private static int lastBindedID = -1;

        public readonly int ID;
        public VAO()
        {
            GL.CreateVertexArrays(1, out ID);
        }

        public void SetElementBuffer(BufferObject sourceBuffer)
        {
            GL.VertexArrayElementBuffer(ID, sourceBuffer.ID);
        }

        public void AddSourceBuffer(BufferObject sourceBuffer, int bindingIndex, int vertexSize, int bufferOffset = 0)
        {
            GL.VertexArrayVertexBuffer(ID, bindingIndex, sourceBuffer.ID, (IntPtr)bufferOffset, vertexSize);
        }

        public void SetAttribFormat(int bindingIndex, int attribIndex, int attribTypeElements, VertexAttribType vertexAttribType, int relativeOffset, bool normalize = false)
        {
            GL.EnableVertexArrayAttrib(ID, attribIndex);
            GL.VertexArrayAttribFormat(ID, attribIndex, attribTypeElements, vertexAttribType, normalize, relativeOffset);
            GL.VertexArrayAttribBinding(ID, attribIndex, bindingIndex);
        }

        public void SetAttribFormatI(int bindingIndex, int attribIndex, int attribTypeElements, VertexAttribType vertexAttribType, int relativeOffset)
        {
            GL.EnableVertexArrayAttrib(ID, attribIndex);
            GL.VertexArrayAttribIFormat(ID, attribIndex, attribTypeElements, vertexAttribType, relativeOffset);
            GL.VertexArrayAttribBinding(ID, attribIndex, bindingIndex);
        }

        public void SetPerBufferDivisor(int bindingIndex, int divisor)
        {
            GL.VertexArrayBindingDivisor(ID, bindingIndex, divisor);
        }

        public void DisableVertexAttribute(int attribIndex)
        {
            GL.DisableVertexArrayAttrib(ID, attribIndex);
        }

        public void Bind()
        {
            if (lastBindedID != ID)
            {
                GL.BindVertexArray(ID);
                lastBindedID = ID;
            }
        }

        public static void Bind(int id)
        {
            if (lastBindedID != id)
            {
                GL.BindVertexArray(id);
                lastBindedID = id;
            }
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(ID);
        }
    }
}
