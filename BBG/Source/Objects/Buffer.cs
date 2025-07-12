using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;

namespace BBOpenGL;

public static partial class BBG
{
    public unsafe class Buffer : IDisposable
    {
        public enum BufferTarget : uint
        {
            DispatchIndirect = OpenTK.Graphics.OpenGL.BufferTarget.DispatchIndirectBuffer,
        }

        public enum BufferBackedBlockTarget : uint
        {
            ShaderStorage = OpenTK.Graphics.OpenGL.BufferTarget.ShaderStorageBuffer,
            Uniform = OpenTK.Graphics.OpenGL.BufferTarget.UniformBuffer,
        }

        public enum MemLocation : uint
        {
            /// <summary>
            /// The buffer resides in DEVICE memory
            /// </summary>
            DeviceLocal = 0,

            /// <summary>
            /// The buffer resides in HOST memory
            /// </summary>
            HostLocal = BufferStorageMask.ClientStorageBit,
        }

        public enum MemAccess : uint
        {
            /// <summary>
            /// The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
            /// Writes by the HOST only become visible to the DEVICE after a call to glMemoryBarrier(CLIENT_MAPPED_BUFFER_BARRIER_BIT) and glFlushMappedBufferRange.
            /// Writes by the DEVICE only become visible to the HOST after a call to glMemoryBarrier(CLIENT_MAPPED_BUFFER_BARRIER_BIT) followed by a wait for glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0)
            /// Note: AMD driver places this in HOST memory!
            /// </summary>
            MappedIncoherent = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapReadBit | BufferStorageMask.MapWriteBit /* MapFlushExplicitBit*/,

            /// <summary>
            /// The buffer must be written or read to by using the mapped memory pointer or read by using the Download functions.
            /// Writes by the DEVICE only become visible to the HOST after waiting for glFenceSync(SYNC_GPU_COMMANDS_COMPLETE, 0).
            /// Note: AMD driver places this in HOST memory!
            /// </summary>
            MappedCoherent = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapCoherentBit | BufferStorageMask.MapReadBit | BufferStorageMask.MapWriteBit,

            /// <summary>
            /// Same as <see cref="MappedIncoherent"/> except that it's write-only AND leverages ReBAR/SAM on AMD (and NVIDIA?) drivers. <br/>
            /// Read buffers are placed in HOST mem by AMD (and NVIDIA?) drivers because:
            /// "there is a massive CPU performance penalty, on the level of 2 to 3 orders of magnitude if you issue a CPU instruction to read from DEVICE_LOCAL
            /// there exists poorly programmed OGL apps who call MapBuffer and similar with flags thats do not properly indicate the app is going to read that memory via CPU
            /// out of caution for these apps we default to HOST memory"
            /// </summary>
            MappedCoherentWriteOnlyReBAR = BufferStorageMask.MapPersistentBit | BufferStorageMask.MapCoherentBit | BufferStorageMask.MapWriteBit,

            /// <summary>
            /// The buffer must be written or read by using the Upload/Download functions.
            /// Synchronization is taken care of by OpenGL.
            /// </summary>
            AutoSync = BufferStorageMask.DynamicStorageBit,
        }

        public int ID { get; private set; }
        public nint Size { get; private set; }
        public void* Memory { get; private set; }

        public BufferBackedBlockTarget BackingBlock { get; private set; }
        public int BlockIndex { get; private set; }

        public Buffer()
        {
            int id = 0;
            GL.CreateBuffers(1, ref id);

            ID = id;
        }

        public void BindToBufferBackedBlock(BufferBackedBlockTarget target, int index)
        {
            GL.BindBufferBase((OpenTK.Graphics.OpenGL.BufferTarget)target, (uint)index, ID);
            BackingBlock = target;
            BlockIndex = index;
        }

        public void InvalidateData()
        {
            GL.InvalidateBufferData(ID);
        }

        public void Allocate(MemLocation memLocation, MemAccess memAccess, nint size)
        {
            Allocate(memLocation, memAccess, size, null);
        }

        public void Allocate(MemLocation memLocation, MemAccess memAccess, nint size, void* data)
        {
            Size = size;
            Memory = null;

            if (Size == 0)
            {
                Dispose();
            }
            else
            {
                // We use staging buffer upload pattern
                // We only do this when the buffer is not mapped, otherwise a glFinish/glFence is required to have the new content be immediately visible.
                // Reasons:
                // * On AMD uploading a lot of data to a GPU-side buffer driver the classical way takes a lot of time.
                //   Staging buffer approach is much faster (800ms vs 30ms, 101MB).
                // * On NVIDIA the uploaded data is kept arround even with GL_NONE flag
                //   https://discord.com/channels/337627185248468993/337629838770700290/1361819795573575730

                bool fastUploadPathCandidate =
                    memLocation == MemLocation.DeviceLocal && 
                    memAccess != MemAccess.MappedCoherent &&
                    memAccess != MemAccess.MappedIncoherent && 
                    memAccess != MemAccess.MappedCoherentWriteOnlyReBAR &&
                    data != null;

                GL.NamedBufferStorage(ID, size, fastUploadPathCandidate ? null : data, (BufferStorageMask)memLocation | (BufferStorageMask)memAccess);
                if (fastUploadPathCandidate)
                {
                    using Buffer stagingBuffer = new Buffer();
                    stagingBuffer.Allocate(MemLocation.HostLocal, MemAccess.AutoSync, size, data);

                    stagingBuffer.CopyTo(this, 0, 0, stagingBuffer.Size);
                }

                if (memAccess == MemAccess.MappedCoherent || memAccess == MemAccess.MappedCoherentWriteOnlyReBAR)
                {
                    Memory = GL.MapNamedBufferRange(ID, 0, size, (MapBufferAccessMask)memAccess);
                }
                else if (memAccess == MemAccess.MappedIncoherent)
                {
                    Memory = GL.MapNamedBufferRange(ID, 0, size, (MapBufferAccessMask)memAccess | MapBufferAccessMask.MapFlushExplicitBit);
                }
            }
        }

        public void UploadData<T>(nint offset, nint size, in T data) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                UploadData(offset, size, ptr);
            }
        }

        public void UploadData(nint offset, nint size, void* data)
        {
            if (size == 0) return;
            GL.NamedBufferSubData(ID, offset, size, data);
        }

        public void DownloadData(nint offset, nint size, void* data)
        {
            if (size == 0) return;
            GL.GetNamedBufferSubData(ID, offset, size, data);
        }

        public void CopyTo(Buffer buffer, nint readOffset, nint writeOffset, nint size)
        {
            if (size == 0) return;
            GL.CopyNamedBufferSubData(ID, buffer.ID, readOffset, writeOffset, size);
        }

        public void Fill(nint offset, nint size, in uint data)
        {
            fixed (void* ptr = &data)
            {
                Fill(Texture.InternalFormat.R32UInt, Texture.PixelFormat.RInteger, Texture.PixelType.Int, offset, size, ptr);
            }
        }
        
        public void Fill(nint offset, nint size, in float data)
        {
            fixed (void* ptr = &data)
            {
                Fill(Texture.InternalFormat.R32Float, Texture.PixelFormat.R, Texture.PixelType.Float, offset, size, ptr);
            }
        }
        
        public void Fill(Texture.InternalFormat internalFormat, Texture.PixelFormat pixelFormat, Texture.PixelType pixelType, nint offset, nint size, void* data)
        {
            if (size == 0) return;
            GL.ClearNamedBufferSubData(ID, (SizedInternalFormat)internalFormat, offset, size, (PixelFormat)pixelFormat, (PixelType)pixelType, data);
        }

        public void FlushMemory(nint offset, nint size)
        {
            if (size == 0) return;
            GL.FlushMappedNamedBufferRange(ID, offset, size);
        }

        public bool IsDeleted()
        {
            return ID == 0;
        }

        public void Dispose()
        {
            if (IsDeleted())
            {
                return;
            }

            if (Memory != null)
            {
                GL.UnmapNamedBuffer(ID);
            }

            GL.DeleteBuffer(ID);
            ID = 0;
        }

        public static void Recreate(ref Buffer buffer, MemLocation memLocation, MemAccess memAccess, nint size)
        {
            Recreate(ref buffer, memLocation, memAccess, size, null);
        }

        public static void Recreate(ref Buffer buffer, MemLocation memLocation, MemAccess memAccess, nint size, void* data = null)
        {
            buffer.Dispose();

            Buffer newBuffer = new Buffer();
            newBuffer.Allocate(memLocation, memAccess, size, data);   

            if (buffer.BackingBlock != 0)
            {
                newBuffer.BindToBufferBackedBlock(buffer.BackingBlock, buffer.BlockIndex);
            }

            buffer = newBuffer;
        }

        public static void Recreate<T>(ref TypedBuffer<T> buffer, MemLocation memLocation, MemAccess memAccess, ReadOnlySpan<T> newValues) where T : unmanaged
        {
            Recreate(ref buffer, memLocation, memAccess, newValues.Length, MemoryMarshal.GetReference(newValues));
        }

        public static void Recreate<T>(ref TypedBuffer<T> buffer, MemLocation memLocation, MemAccess memAccess, nint count, in T data) where T : unmanaged
        {
            fixed (void* ptr = &data)
            {
                Recreate(ref buffer, memLocation, memAccess, count, ptr);
            }
        }

        public static void Recreate<T>(ref TypedBuffer<T> buffer, MemLocation memLocation, MemAccess memAccess, nint count) where T : unmanaged
        {
            Recreate(ref buffer, memLocation, memAccess, count, null);
        }

        public static void Recreate<T>(ref TypedBuffer<T> buffer, MemLocation memLocation, MemAccess memAccess, nint count, void* data) where T : unmanaged
        {
            ref Buffer baseBuffer = ref Unsafe.As<TypedBuffer<T>, Buffer>(ref buffer);
            Recreate(ref baseBuffer, memLocation, memAccess, sizeof(T) * count, data);
        }
    }

    public unsafe class TypedBuffer<T> : Buffer where T : unmanaged
    {
        public new T* Memory => (T*)base.Memory;
        public int NumElements => (int)(Size / sizeof(T));

        public TypedBuffer()
            : base()
        {

        }

        public void AllocateElements(MemLocation memLocation, MemAccess memAccess, ReadOnlySpan<T> data)
        {
            AllocateElements(memLocation, memAccess, data.Length, MemoryMarshal.GetReference(data));
        }

        public void AllocateElements(MemLocation memLocation, MemAccess memAccess, nint count, in T data)
        {
            fixed (void* ptr = &data)
            {
                AllocateElements(memLocation, memAccess, count, ptr);
            }
        }

        public void AllocateElements(MemLocation memLocation, MemAccess memAccess, nint count)
        {
            AllocateElements(memLocation, memAccess, count, null);
        }

        public void AllocateElements(MemLocation memLocation, MemAccess memAccess, nint count, void* data)
        {
            Allocate(memLocation, memAccess, sizeof(T) * count, data);
        }

        public void UploadElements(ReadOnlySpan<T> data, nint startIndex = 0)
        {
            UploadElements(startIndex, data.Length, MemoryMarshal.GetReference(data));
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

        public void DownloadElements(out T[] values)
        {
            values = new T[NumElements];
            DownloadElements(values, 0);
        }

        public void DownloadElements(Span<T> data, nint startIndex = 0)
        {
            DownloadElements(startIndex, data.Length, ref MemoryMarshal.GetReference(data));
        }

        public void DownloadElements(nint startIndex, nint count, ref T data)
        {
            fixed (void* ptr = &data)
            {
                DownloadData(startIndex * sizeof(T), count * sizeof(T), ptr);
            }
        }

        public void CopyElementsTo(TypedBuffer<T> buffer, nint readElementOffset, nint writeElementOffset, nint count)
        {
            CopyTo(buffer, readElementOffset * sizeof(T), writeElementOffset * sizeof(T), count * sizeof(T));
        }

        public void CopyElementsTo(TypedBuffer<T> buffer, nint readElementOffset = 0, nint writeElementOffset = 0)
        {
            CopyElementsTo(buffer, readElementOffset, writeElementOffset, NumElements);
        }
    }
}
