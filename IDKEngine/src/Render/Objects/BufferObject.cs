using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    public class BufferObject : IDisposable
    {
        public readonly int ID;
        public int Size { get; private set; }

        public BufferObject()
        {
            GL.CreateBuffers(1, out ID);
        }

        public void BindBufferBase(BufferRangeTarget target, int index)
        {
            GL.BindBufferBase(target, index, ID);
        }

        public void Bind(BufferTarget bufferTarget)
        {
            GL.BindBuffer(bufferTarget, ID);
        }

        public unsafe void SubData<T>(int offset, int size, in T data) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                GL.NamedBufferSubData(ID, offset, size, (IntPtr)ptr);
            }
        }
        public void SubData<T>(int offset, int size, T[] data) where T : unmanaged
        {
            GL.NamedBufferSubData(ID, offset, size, data);
        }
        public void SubData(int offset, int size, IntPtr data)
        {
            GL.NamedBufferSubData(ID, offset, size, data);
        }

        public unsafe void MutableAllocate<T>(int size, in T data) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                GL.NamedBufferData(ID, size, (IntPtr)ptr, BufferUsageHint.StaticDraw);
            }
            Size = size;
        }
        public void MutableAllocate<T>(int size, T[] data) where T : unmanaged
        {
            GL.NamedBufferData(ID, size, data, BufferUsageHint.StaticDraw);
            Size = size;
        }
        public void MutableAllocate(int size, IntPtr data)
        {
            GL.NamedBufferData(ID, size, data, BufferUsageHint.StaticDraw);
            Size = size;
        }

        public unsafe void ImmutableAllocate<T>(int size, in T data, BufferStorageFlags bufferStorageFlags) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                GL.NamedBufferStorage(ID, size, (IntPtr)ptr, bufferStorageFlags);
            }
            Size = size;
        }
        public void ImmutableAllocate<T>(int size, T[] data, BufferStorageFlags bufferStorageFlags) where T : unmanaged
        {
            GL.NamedBufferStorage(ID, size, data, bufferStorageFlags);
            Size = size;
        }
        public void ImmutableAllocate(int size, IntPtr data, BufferStorageFlags bufferStorageFlags)
        {
            GL.NamedBufferStorage(ID, size, data, bufferStorageFlags);
            Size = size;
        }

        public void GetSubData<T>(int offset, int size, out T data) where T : unmanaged
        {
            data = new T();
            GL.GetNamedBufferSubData(ID, offset, size, ref data);
        }
        public void GetSubData<T>(int offset, int size, T[] data) where T : unmanaged
        {
            GL.GetNamedBufferSubData(ID, offset, size, data);
        }
        public void GetSubData(int offset, int size, IntPtr data)
        {
            GL.GetNamedBufferSubData(ID, offset, size, data);
        }

        public unsafe void Clear(int offset, int size, uint value)
        {
            GL.ClearNamedBufferSubData(ID, PixelInternalFormat.R32ui, offset, size, PixelFormat.RedInteger, PixelType.UnsignedInt, (nint)(&value));
        }

        public void Dispose()
        {
            GL.DeleteBuffer(ID);
        }
    }
}
