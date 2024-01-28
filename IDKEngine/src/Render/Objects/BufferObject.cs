using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    public class BufferObject : IDisposable
    {
        public enum BufferStorageType
        {
            // The buffer resides in DEVICE memory and can only filled from the CPU at the time of creation
            DeviceLocal = BufferStorageFlags.None,

            // The buffer must be read & write by using the mapped memory pointer.
            // Writes by the HOST only become visible to the DEVICE after a call to glFlushMappedBufferRange.
            // Writes by the DEVICE only become visible to the HOST after a call to glMemoryBarrier(CLIENT_MAPPED_BUFFER_BARRIER_BIT)
            // followed by glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0)
            DeviceLocalHostVisible = BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapReadBit | BufferStorageFlags.MapWriteBit,

            // The buffer must be read & write by using the mapped memory pointer.
            // Writes by the DEVICE only become visible to the HOST after a call to glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0).
            DeviceLocalHostVisibleCoherent = BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapCoherentBit | BufferStorageFlags.MapReadBit | BufferStorageFlags.MapWriteBit,

            // The buffer may be updated or downloaded from using the given functions.
            // Synchronization is taken care of by OpenGL. 
            Dynamic = BufferStorageFlags.DynamicStorageBit
        }

        public readonly int ID;
        public nint Size { get; private set; }
        public IntPtr MappedMemory { get; private set; }

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

        public void ImmutableAllocate(BufferStorageType type, nint size, IntPtr data)
        {
            GL.NamedBufferStorage(ID, size, data, (BufferStorageFlags)type);
            Size = size;

            MappedMemory = IntPtr.Zero;
            
            if (type == BufferStorageType.DeviceLocalHostVisible)
            {
                MappedMemory = GL.MapNamedBufferRange(ID, 0, size, (BufferAccessMask)type | BufferAccessMask.MapFlushExplicitBit);
            }
            else if (type == BufferStorageType.DeviceLocalHostVisibleCoherent)
            {
                MappedMemory = GL.MapNamedBufferRange(ID, 0, size, (BufferAccessMask)type);
            }
        }
        public void MutableAllocate(nint size, IntPtr data)
        {
            GL.NamedBufferData(ID, size, data, BufferUsageHint.StaticDraw);
            Size = size;
        }

        public void UploadData(nint offset, nint size, IntPtr data)
        {
            GL.NamedBufferSubData(ID, offset, size, data);
        }
        public unsafe void UploadData<T>(nint offset, nint size, in T data) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                GL.NamedBufferSubData(ID, offset, size, (nint)ptr);
            }
        }
        public void DownloadData(nint offset, nint size, IntPtr data)
        {
            GL.GetNamedBufferSubData(ID, offset, size, data);
        }

        public unsafe void SimpleClear(nint offset, nint size, IntPtr data)
        {
            Clear(PixelInternalFormat.R32f, PixelFormat.Red, PixelType.Float, offset, size, data);
        }
        public unsafe void Clear(PixelInternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType, nint offset, nint size, IntPtr data)
        {
            GL.ClearNamedBufferSubData(ID, internalFormat, offset, size, pixelFormat, pixelType, data);
        }

        public void Dispose()
        {
            if (MappedMemory != IntPtr.Zero)
            {
                GL.UnmapNamedBuffer(ID);
            }
            GL.DeleteBuffer(ID);
        }
    }

    public unsafe class TypedBuffer<T> : BufferObject
        where T : unmanaged
    {

        public new T* MappedMemory => (T*)base.MappedMemory;

        public TypedBuffer()
            : base()
        {

        }

        public void ImmutableAllocate(BufferStorageType type, ReadOnlySpan<T> data)
        {
            ImmutableAllocate(type, data.Length, data[0]);
        }
        public void ImmutableAllocate(BufferStorageType type, nint count, in T data)
        {
            fixed (void* ptr = &data)
            {
                ImmutableAllocate(type, sizeof(T) * count, (nint)ptr);
            }
        }
        public void ImmutableAllocate(BufferStorageType type, nint count)
        {
            ImmutableAllocate(type, sizeof(T) * count, IntPtr.Zero);
        }

        public void MutableAllocate(ReadOnlySpan<T> data)
        {
            MutableAllocate(data.Length, data[0]);
        }
        public void MutableAllocate(nint count, in T data)
        {
            fixed (void* ptr = &data)
            {
                MutableAllocate(sizeof(T) * count, (nint)ptr);
            }
        }

        public void UploadElements(ReadOnlySpan<T> data, nint startIndex = 0)
        {
            UploadElements(startIndex, data.Length, data[0]);
        }
        public void UploadElements(in T data, nint startIndex = 0)
        {
            UploadElements(startIndex, 1, data);
        }
        public void UploadElements(nint startIndex, nint count, in T data)
        {
            fixed (void* ptr = &data)
            {
                UploadData(startIndex * sizeof(T), count * sizeof(T), (nint)ptr);
            }
        }

        public void DownloadElements(Span<T> data, nint startIndex = 0)
        {
            DownloadElements(startIndex, data.Length, ref data[0]);
        }
        public void DownloadElements(nint startIndex, nint count, ref T data)
        {
            fixed (void* ptr = &data)
            {
                DownloadData(startIndex * sizeof(T), count * sizeof(T), (nint)ptr);
            }
        }

        public int GetNumElements()
        {
            return (int)(Size / sizeof(T));
        }
    }
}
