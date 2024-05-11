using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public unsafe class BufferObject : IDisposable
        {
            public enum BufferTarget : uint
            {
                ShaderStorage = OpenTK.Graphics.OpenGL.BufferTarget.ShaderStorageBuffer,
                Uniform = OpenTK.Graphics.OpenGL.BufferTarget.UniformBuffer,
                PixelUnpack = OpenTK.Graphics.OpenGL.BufferTarget.PixelUnpackBuffer,
                DispatchIndirect = OpenTK.Graphics.OpenGL.BufferTarget.DispatchIndirectBuffer,
            }

            public enum MemLocation : uint
            {
                // The buffer resides in DEVICE memory
                DeviceLocal = 0,

                // The buffer resides in HOST memory
                HostLocal = BufferStorageMask.ClientStorageBit,
            }

            public enum MemAccess : uint
            {
                // The buffer can not be written to from the HOST except at the time of creation.
                // It can be read by using the Download functions.
                None = 0,

                // The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
                // Writes by the HOST only become visible to the DEVICE after a call to glFlushMappedBufferRange.
                // Writes by the DEVICE only become visible to the HOST after a call to glMemoryBarrier(CLIENT_MAPPED_BUFFER_BARRIER_BIT)
                // followed by glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0)
                MappedIncoherent = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapReadBit | BufferStorageMask.MapWriteBit/* | memAccessMask.MapFlushExplicitBit*/,

                // The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
                // Writes by the DEVICE only become visible to the HOST after a call to glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0).
                MappedCoherent = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapCoherentBit | BufferStorageMask.MapReadBit | BufferStorageMask.MapWriteBit,

                // The buffer must be written or read by to using the Upload/Download functions.
                // Synchronization is taken care of by OpenGL.
                Synced = BufferStorageMask.DynamicStorageBit,
            }

            public readonly int ID;
            public nint Size { get; private set; }
            public void* MappedMemory { get; private set; }

            public BufferObject()
            {
                GL.CreateBuffers(1, ref ID);
            }

            public void BindBufferBase(BufferTarget target, int index)
            {
                GL.BindBufferBase((OpenTK.Graphics.OpenGL.BufferTarget)target, (uint)index, ID);
            }

            public void ImmutableAllocate(MemLocation memLocation, MemAccess memAccess, nint size, void* data = null)
            {
                GL.NamedBufferStorage(ID, size, (nint)data, (BufferStorageMask)memLocation | (BufferStorageMask)memAccess);
                Size = size;

                MappedMemory = null;

                if (memAccess == MemAccess.MappedIncoherent || memAccess == MemAccess.MappedIncoherent)
                {
                    MappedMemory = GL.MapNamedBufferRange(ID, 0, size, (MapBufferAccessMask)memAccess);
                }
            }

            public void MutableAllocate(nint size, void* data = null)
            {
                GL.NamedBufferData(ID, size, (nint)data, VertexBufferObjectUsage.StaticDraw);
                Size = size;
            }

            public void UploadData(nint offset, nint size, void* data)
            {
                GL.NamedBufferSubData(ID, offset, size, (nint)data);
            }

            public void UploadData<T>(nint offset, nint size, in T data) where T : unmanaged
            {
                fixed (void* ptr = &data)
                {
                    UploadData(offset, size, ptr);
                }
            }

            public void DownloadData(nint offset, nint size, void* data)
            {
                GL.GetNamedBufferSubData(ID, offset, size, (nint)data);
            }

            public void SimpleClear(nint offset, nint size, float* data)
            {
                Clear(SizedInternalFormat.R32f, PixelFormat.Red, PixelType.Float, offset, size, data);
            }
            public void Clear(SizedInternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType, nint offset, nint size, void* data)
            {
                GL.ClearNamedBufferSubData(ID, internalFormat, offset, size, pixelFormat, pixelType, (nint)data);
            }

            public void Dispose()
            {
                if (MappedMemory != null)
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
                    ImmutableAllocateElements(memLocation, memAccess, count, ptr);
                }
            }
            public void ImmutableAllocateElements(MemLocation memLocation, MemAccess memAccess, nint count, void* data = null)
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
                    MutableAllocateElements(count, ptr);
                }
            }
            public void MutableAllocateElements(nint count, void* data = null)
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
                    UploadData(startIndex * sizeof(T), count * sizeof(T), ptr);
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
                    DownloadData(startIndex * sizeof(T), count * sizeof(T), ptr);
                }
            }

            public int GetNumElements()
            {
                return (int)(Size / sizeof(T));
            }
        }
    }
}
