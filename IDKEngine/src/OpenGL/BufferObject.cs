using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.OpenGL
{
    public class BufferObject : IDisposable
    {
        public enum MemLocation
        {
            // The buffer resides in DEVICE memory
            DeviceLocal = BufferStorageFlags.None,

            // The buffer resides in HOST memory
            HostLocal = BufferStorageFlags.ClientStorageBit,
        }
        public enum MemAccess
        {
            // The buffer can not be written to from the HOST except at the time of creation.
            // It can be read by using the Download functions.
            None = BufferStorageFlags.None,

            // The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
            // Writes by the HOST only become visible to the DEVICE after a call to glFlushMappedBufferRange.
            // Writes by the DEVICE only become visible to the HOST after a call to glMemoryBarrier(CLIENT_MAPPED_BUFFER_BARRIER_BIT)
            // followed by glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0)
            MappedIncoherent = BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapReadBit | BufferStorageFlags.MapWriteBit/* | memAccessMask.MapFlushExplicitBit*/,

            // The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
            // Writes by the DEVICE only become visible to the HOST after a call to glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0).
            MappedCoherent = BufferStorageFlags.MapPersistentBit | BufferStorageFlags.MapCoherentBit | BufferStorageFlags.MapReadBit | BufferStorageFlags.MapWriteBit,

            // The buffer must be written or read by to using the Upload/Download functions.
            // Synchronization is taken care of by OpenGL.
            Synced = BufferStorageFlags.DynamicStorageBit,
        }

        public readonly int ID;
        public nint Size { get; private set; }
        public nint MappedMemory { get; private set; }

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

        public void ImmutableAllocate(MemLocation memLocation, MemAccess memAccess, nint size, nint data = 0)
        {
            GL.NamedBufferStorage(ID, size, data, (BufferStorageFlags)memLocation | (BufferStorageFlags)memAccess);
            Size = size;

            MappedMemory = nint.Zero;

            if (memAccess == MemAccess.MappedIncoherent)
            {
                MappedMemory = GL.MapNamedBufferRange(ID, 0, size, (BufferAccessMask)memAccess | BufferAccessMask.MapFlushExplicitBit);
            }
            else if (memAccess == MemAccess.MappedCoherent)
            {
                MappedMemory = GL.MapNamedBufferRange(ID, 0, size, (BufferAccessMask)memAccess);
            }
        }
        public void MutableAllocate(nint size, nint data = 0)
        {
            GL.NamedBufferData(ID, size, data, BufferUsageHint.StaticDraw);
            Size = size;
        }

        public void UploadData(nint offset, nint size, nint data)
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
        public void DownloadData(nint offset, nint size, nint data)
        {
            GL.GetNamedBufferSubData(ID, offset, size, data);
        }

        public void SimpleClear(nint offset, nint size, nint data)
        {
            Clear(PixelInternalFormat.R32f, PixelFormat.Red, PixelType.Float, offset, size, data);
        }
        public void Clear(PixelInternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType, nint offset, nint size, nint data)
        {
            GL.ClearNamedBufferSubData(ID, internalFormat, offset, size, pixelFormat, pixelType, data);
        }

        public void Dispose()
        {
            if (MappedMemory != nint.Zero)
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

        public void ImmutableAllocateElements(MemLocation memLocation, MemAccess memAccess, ReadOnlySpan<T> data)
        {
            ImmutableAllocateElements(memLocation, memAccess, data.Length, data[0]);
        }
        public void ImmutableAllocateElements(MemLocation memLocation, MemAccess memAccess, nint count, in T data)
        {
            fixed (void* ptr = &data)
            {
                ImmutableAllocateElements(memLocation, memAccess, count, (nint)ptr);
            }
        }
        public void ImmutableAllocateElements(MemLocation memLocation, MemAccess memAccess, nint count, nint data = 0)
        {
            ImmutableAllocate(memLocation, memAccess, sizeof(T) * count, data);
        }

        public void MutableAllocateElements(ReadOnlySpan<T> data)
        {
            MutableAllocateElements(data.Length, data[0]);
        }
        public void MutableAllocateElements(nint count, in T data)
        {
            fixed (void* ptr = &data)
            {
                MutableAllocateElements(count, (nint)ptr);
            }
        }
        public void MutableAllocateElements(nint count, nint data = 0)
        {
            MutableAllocate(sizeof(T) * count, data);
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
