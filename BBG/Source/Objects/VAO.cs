using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public class VAO : IDisposable
        {
            public enum VertexAttribType : uint
            {
                Float = OpenTK.Graphics.OpenGL.VertexAttribType.Float,
            }

            public readonly int ID;
            public VAO()
            {
                GL.CreateVertexArrays(1, ref ID);
            }

            public void SetElementBuffer(BufferObject sourceBuffer)
            {
                GL.VertexArrayElementBuffer(ID, sourceBuffer.ID);
            }

            public void AddSourceBuffer(BufferObject sourceBuffer, int bindingIndex, int vertexSize, int bufferOffset = 0)
            {
                GL.VertexArrayVertexBuffer(ID, (uint)bindingIndex, sourceBuffer.ID, bufferOffset, vertexSize);
            }

            public void SetAttribFormat(int bindingIndex, int attribIndex, int attribTypeElements, VertexAttribType vertexAttribType, int relativeOffset, bool normalize = false)
            {
                GL.EnableVertexArrayAttrib(ID, (uint)attribIndex);
                GL.VertexArrayAttribFormat(ID, (uint)attribIndex, attribTypeElements, (OpenTK.Graphics.OpenGL.VertexAttribType)vertexAttribType, normalize, (uint)relativeOffset);
                GL.VertexArrayAttribBinding(ID, (uint)attribIndex, (uint)bindingIndex);
            }

            public void Dispose()
            {
                GL.DeleteVertexArray(ID);
            }
        }
    }
}
