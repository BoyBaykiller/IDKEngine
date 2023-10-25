using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    public class BufferObject : IDisposable
    {
        public readonly int ID;
        public nint Size { get; private set; }

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

        public void SubData<T>(nint offset, nint size, T[] data) where T : unmanaged
        {
            SubData(offset, size, data[0]);
        }
        public unsafe void SubData<T>(nint offset, nint size, in T data) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                SubData(offset, size, (IntPtr)ptr);
            }
        }
        public void SubData(nint offset, nint size, IntPtr data)
        {
            GL.NamedBufferSubData(ID, offset, size, data);
        }

        public void MutableAllocate<T>(nint size, T[] data) where T : unmanaged
        {
            MutableAllocate(size, data[0]);
        }
        public unsafe void MutableAllocate<T>(nint size, in T data) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                MutableAllocate(size, (IntPtr)ptr);
            }
        }
        public void MutableAllocate(nint size, IntPtr data)
        {
            GL.NamedBufferData(ID, size, data, BufferUsageHint.StaticDraw);
            Size = size;
        }

        public void ImmutableAllocate<T>(nint size, T[] data, BufferStorageFlags bufferStorageFlags) where T : unmanaged
        {
            ImmutableAllocate(size, data[0], bufferStorageFlags);
        }
        public unsafe void ImmutableAllocate<T>(nint size, in T data, BufferStorageFlags bufferStorageFlags) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                ImmutableAllocate(size, (IntPtr)ptr, bufferStorageFlags);
            }
        }
        public void ImmutableAllocate(nint size, IntPtr data, BufferStorageFlags bufferStorageFlags)
        {
            GL.NamedBufferStorage(ID, size, data, bufferStorageFlags);
            Size = size;
        }

        public unsafe void GetSubData<T>(nint offset, out T data) where T : unmanaged
        {
            data = new T();
            GL.GetNamedBufferSubData(ID, offset, sizeof(T), ref data);
        }
        public void GetSubData<T>(nint offset, nint size, T[] data) where T : unmanaged
        {
            GL.GetNamedBufferSubData(ID, offset, size, data);
        }
        public void GetSubData(nint offset, nint size, IntPtr data)
        {
            GL.GetNamedBufferSubData(ID, offset, size, data);
        }

        public unsafe void Clear(nint offset, nint size, uint value)
        {
            GL.ClearNamedBufferSubData(ID, PixelInternalFormat.R32ui, offset, size, PixelFormat.RedInteger, PixelType.UnsignedInt, (nint)(&value));
        }

        public void Dispose()
        {
            GL.DeleteBuffer(ID);
        }
    }
}
